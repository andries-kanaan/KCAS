using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Data;

public sealed class SanctionsAutomationLock
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}

public sealed record SanctionsAutomationStatus(bool Enabled, int IntervalMinutes, SanctionsSourceCheck? LatestCheck,
    SanctionsSourceCheck? LastSuccessfulCheck, bool IsStale);

public sealed class SanctionsAutomationService(IDbContextFactory<ApplicationDbContext> factory, ISanctionsFeed feed,
    IOptions<SanctionsAutomationOptions> options, SanctionsAutomationLock runLock)
{
    public const string Performer = "KCAS automated sanctions";
    public const string SystemUserId = "KCAS-SANCTIONS-JOB";
    private const string Limitations = "Deterministic names, recorded aliases and available identifiers only; no identity authentication, universal transliteration, PEP/PIP search or management approval. Potential candidates require human resolution.";

    public async Task<SanctionsAutomationStatus> StatusAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var latest = await db.SanctionsSourceChecks.AsNoTracking().Include(x => x.Snapshot).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var success = await db.SanctionsSourceChecks.AsNoTracking().Include(x => x.Snapshot)
            .Where(x => x.Outcome != "Failed").OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        return new(options.Value.Enabled, options.Value.PollIntervalMinutes, latest, success,
            success is null || success.CompletedAtUtc < DateTime.UtcNow.AddHours(-options.Value.MaximumSourceAgeHours));
    }

    public async Task<ComplianceControlReminder?> ReminderAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id)) return null;
        var status = await StatusAsync(principal);
        return status.LatestCheck?.Outcome == "Failed" || status.Enabled && status.IsStale
            ? new("Official sanctions source needs attention", 1, "/compliance/sanctions") : null;
    }

    public async Task<SanctionsSourceCheck?> CheckNowAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        return await RunAsync(cancellationToken);
    }

    public async Task<SanctionsSourceSnapshot?> SnapshotAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        await ComplianceWorkflowAccess.ActorAsync(db, principal, KcasPermissions.ComplianceView);
        return await db.SanctionsSourceSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    }

    internal async Task<SanctionsSourceCheck?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || !await runLock.Gate.WaitAsync(0, cancellationToken)) return null;
        var start = DateTime.UtcNow;
        try
        {
            byte[] payload;
            ParsedSanctionsList parsed;
            try
            {
                OfficialSanctionsFeed.ValidateUrl(options.Value.SourceUrl);
                payload = await feed.DownloadAsync(cancellationToken);
                parsed = SanctionsList.Parse(payload, options.Value.MinimumIndividuals, options.Value.MinimumEntities);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return await RecordFailureAsync(start, "Official XML retrieval/validation failed: " + ex.GetType().Name + ". " + ex.Message, cancellationToken);
            }
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var previous = await db.SanctionsSourceSnapshots.OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            // An abrupt contraction needs review; never clear a silently truncated feed.
            if (previous is not null && (parsed.Entries.Count(x => x.Kind == "Individual") < previous.Individuals * .8 ||
                                        parsed.Entries.Count(x => x.Kind == "Entity") < previous.Entities * .8))
            {
                await tx.RollbackAsync(cancellationToken);
                return await RecordFailureAsync(start, "The official list lost more than 20% of an individual/entity section. Review the source before relying on it.", cancellationToken);
            }
            var changed = previous is null || previous.ContentSha256 != parsed.Sha256 || previous.SourceUrl != options.Value.SourceUrl;
            var snapshot = previous;
            if (changed)
            {
                var now = DateTime.UtcNow;
                var version = $"FIC/UN XML SHA256 {parsed.Sha256} {now:yyyyMMddTHHmmssfffffffZ}";
                var recipients = await RecipientsAsync(db);
                var batch = new ClientSanctionsBatch { SourceVersion = version, SourceUrl = options.Value.SourceUrl,
                    SourcePublishedAtUtc = parsed.PublishedAtUtc ?? now, CreatedBy = Performer,
                    Reason = parsed.PublishedAtUtc.HasValue ? "Validated official XML update; preserve snapshot and actual check results." :
                        "Validated official XML update. Publisher timestamp is not provided; the source-date field is the first retrieval baseline, not an invented publication date.",
                    RecipientUserIdsJson = JsonSerializer.Serialize(recipients) };
                var employees = new EmployeeTfsBatch { SourceVersion = version, SourceUrl = options.Value.SourceUrl,
                    CreatedByUserId = SystemUserId, Reason = batch.Reason };
                db.ClientSanctionsBatches.Add(batch); db.EmployeeTfsBatches.Add(employees);
                await db.SaveChangesAsync(cancellationToken);
                snapshot = new() { SourceUrl = options.Value.SourceUrl, ContentSha256 = parsed.Sha256, Payload = payload,
                    RetrievedAtUtc = now, PublishedAtUtc = parsed.PublishedAtUtc, Individuals = parsed.Entries.Count(x => x.Kind == "Individual"),
                    Entities = parsed.Entries.Count(x => x.Kind == "Entity"), ClientSanctionsBatchId = batch.Id, EmployeeTfsBatchId = employees.Id };
                db.SanctionsSourceSnapshots.Add(snapshot);
                await db.SaveChangesAsync(cancellationToken);
                Audit(db, nameof(SanctionsSourceSnapshot), snapshot.Id, "OfficialListChanged", new { parsed.Sha256, snapshot.Individuals, snapshot.Entities, recipients });
            }
            var source = snapshot!;
            var sourceBatch = await db.ClientSanctionsBatches.SingleAsync(x => x.Id == source.ClientSanctionsBatchId, cancellationToken);
            await ClientSanctionsCoverageService.SynchronizeAsync(db, sourceBatch, Performer, "Official-list automation: refresh the actual current identity scope.");
            await db.SaveChangesAsync(cancellationToken);
            var check = new SanctionsSourceCheck { StartedAtUtc = start, SourceUrl = options.Value.SourceUrl,
                MaximumSourceAgeHours = options.Value.MaximumSourceAgeHours,
                SanctionsSourceSnapshotId = source.Id, Outcome = changed ? "Updated" : "Unchanged",
                Detail = changed ? "Validated changed source; population screening performed." : "Validated unchanged source; new/changed identities and outstanding automated scopes checked." };
            await ScreenClientsAsync(db, source, sourceBatch, parsed, check, cancellationToken);
            await ScreenEmployeesAsync(db, source, sourceBatch, parsed, check, cancellationToken);
            var failureTasks = await db.ComplianceTasks.Where(x => x.LinkedEntityType == nameof(SanctionsSourceCheck) && x.Status != ComplianceStatuses.Closed).ToListAsync(cancellationToken);
            foreach (var task in failureTasks) { task.Status = ComplianceStatuses.Closed; task.ClosedAtUtc = DateTime.UtcNow; task.ClosureReason = "A valid official source was subsequently retrieved; subject findings remain separately controlled."; }
            check.CompletedAtUtc = DateTime.UtcNow;
            db.SanctionsSourceChecks.Add(check);
            await db.SaveChangesAsync(cancellationToken);
            Audit(db, nameof(SanctionsSourceCheck), check.Id, "OfficialListChecked", new { check.Outcome, check.SubjectsChecked, check.NoCandidates, check.NeedsReview, source.ContentSha256 });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return check;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return await RecordFailureAsync(start, "Official-source screening transaction failed: " + ex.GetType().Name + ". " + ex.Message, cancellationToken);
        }
        finally { runLock.Gate.Release(); }
    }

    private static async Task ScreenClientsAsync(ApplicationDbContext db, SanctionsSourceSnapshot snapshot, ClientSanctionsBatch batch,
        ParsedSanctionsList list, SanctionsSourceCheck check, CancellationToken ct)
    {
        var subjects = await db.ClientSanctionsSubjects.Include(x => x.Client).Include(x => x.Task)
            .Where(x => x.ClientSanctionsBatchId == batch.Id && x.IsCurrent).ToListAsync(ct);
        var clientIds = subjects.Select(x => x.ClientId).Distinct().ToList();
        var evidence = await db.ClientEvidenceItems.Where(x => clientIds.Contains(x.ClientId) && x.EvidenceType == "SanctionsTfs").ToListAsync(ct);
        var records = await db.ClientSanctionsCoverageRecords.Include(x => x.Evidence).Where(x => x.Subject.ClientSanctionsBatchId == batch.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
        var automatic = (await db.SanctionsAutomatedResults.Where(x => x.ClientSanctionsSubjectId != null &&
            x.SanctionsSourceSnapshotId == snapshot.Id).OrderByDescending(x => x.Id).ToListAsync(ct))
            .GroupBy(x => x.ClientSanctionsSubjectId!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var subject in subjects)
        {
            var last = records.FirstOrDefault(x => x.ClientSanctionsSubjectId == subject.Id);
            if (last?.Outcome == "Excluded") continue;
            if (last?.Evidence is { } existing && last.EvidenceFingerprint == ClientSanctionsCoverageService.Fingerprint(existing) &&
                ClientSanctionsCoverageService.Supports(subject, existing, batch.SourcePublishedAtUtc) &&
                existing.ScreeningOutcome == ClientEvidenceScreeningOutcomes.NoMatch && !existing.EscalationRequired &&
                existing.Status == ClientEvidenceStatuses.Verified && existing.SelectionStatus == ClientEvidenceSelectionStatuses.Current &&
                existing.SupersededByClientEvidenceItemId is null &&
                (existing.ExpiryDate is null || existing.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today))) continue;
            automatic.TryGetValue(subject.Id, out var priorAuto);
            if (priorAuto is not null && priorAuto.ScopeHash == subject.ScopeHash && last?.Outcome != "ScopeChanged" &&
                priorAuto.Outcome != "NoMatch") continue;
            var scope = ClientScope(subject);
            var match = SanctionsList.Match(list, scope);
            if (match.Outcome == "NoMatch" && evidence.Any(e => e.ClientId == subject.ClientId && e.ScreeningSubjectName == subject.SubjectName &&
                e.ScreeningSubjectType == subject.SubjectType && e.ClientRelatedPartyId == subject.ClientRelatedPartyId &&
                e.Status == ClientEvidenceStatuses.Verified && (e.ScreeningOutcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch ||
                    e.ScreeningOutcome == ClientEvidenceScreeningOutcomes.PossibleMatch && !e.SupersededByClientEvidenceItemId.HasValue &&
                    !evidence.Any(clear => clear.ClientId == e.ClientId && clear.ScreeningSubjectName == e.ScreeningSubjectName &&
                        clear.ScreeningSubjectType == e.ScreeningSubjectType && clear.ClientRelatedPartyId == e.ClientRelatedPartyId &&
                        clear.Status == ClientEvidenceStatuses.Verified && clear.ScreeningOutcome == ClientEvidenceScreeningOutcomes.NoMatch &&
                        !clear.EscalationRequired && clear.ScreeningPerformedBy != Performer && clear.ScreeningReviewedAtUtc > e.ScreeningReviewedAtUtc))))
                match = new("ManualReviewRequired", "An earlier possible/confirmed human finding remains. Automation cannot resolve or overwrite it.", []);
            var now = ComplianceWorkflowAccess.ActualTime(DateTime.UtcNow);
            var result = new SanctionsAutomatedResult { SanctionsSourceSnapshotId = snapshot.Id, ClientSanctionsSubjectId = subject.Id,
                ScopeHash = subject.ScopeHash, ScopeJson = JsonSerializer.Serialize(scope), Outcome = match.Outcome,
                CandidatesJson = JsonSerializer.Serialize(match.Candidates), Finding = match.Finding + " " + Limitations, PerformedAtUtc = now };
            db.SanctionsAutomatedResults.Add(result);
            ClientEvidenceItem? item = null;
            if (match.Outcome is "NoMatch" or "PossibleMatch")
            {
                item = new() { ClientId = subject.ClientId, ClientRelatedPartyId = subject.ClientRelatedPartyId, EvidenceType = "SanctionsTfs",
                    Title = "Official XML sanctions check", Status = ClientEvidenceStatuses.Verified, OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
                    SelectionStatus = ClientEvidenceSelectionStatuses.Current, Reviewer = Performer, ScreeningPerformedBy = Performer,
                    ScreeningReviewedAtUtc = now, ScreeningReviewDate = DateOnly.FromDateTime(now), VerifiedDate = DateOnly.FromDateTime(now),
                    ScreeningSubjectName = subject.SubjectName, ScreeningSubjectType = subject.SubjectType, ScreeningOutcome = match.Outcome,
                    ScreeningSources = $"{snapshot.SourceUrl}\n{batch.SourceVersion}\n{SanctionsList.MatcherVersion}; retained snapshot {snapshot.Id}",
                    Notes = result.Finding + "\nRecorded scope: " + result.ScopeJson + "\nCandidates: " + result.CandidatesJson,
                    EscalationRequired = match.Outcome != "NoMatch", UpdatedBy = Performer, UpdatedAtUtc = now, VerificationPolicy = "AutomatedOfficialSanctions" };
                db.ClientEvidenceItems.Add(item); await db.SaveChangesAsync(ct);
                result.ClientEvidenceItemId = item.Id;
            }
            db.ClientSanctionsCoverageRecords.Add(new() { ClientSanctionsSubjectId = subject.Id, ClientEvidenceItemId = item?.Id,
                EvidenceFingerprint = item is null ? null : ClientSanctionsCoverageService.Fingerprint(item), Outcome = match.Outcome,
                Reason = result.Finding, RecordedBy = Performer, RecordedAtUtc = now });
            await ClientSanctionsCoverageService.UpdateTaskAsync(db, subject, match.Outcome == "NoMatch", result.Finding);
            if (subject.Task is { } task)
            {
                task.Description = result.Finding;
                task.UpdatedBy = Performer;
            }
            check.SubjectsChecked++;
            if (match.Outcome == "NoMatch") check.NoCandidates++; else check.NeedsReview++;
            Audit(db, nameof(ClientSanctionsSubject), subject.Id, "AutomatedTfsResult", new { subject.ScopeHash, result.Outcome, SnapshotId = snapshot.Id, EvidenceId = item?.Id, match.Candidates });
        }
        batch.Version = ComplianceWorkflowAccess.NewVersion();
    }

    private static async Task ScreenEmployeesAsync(ApplicationDbContext db, SanctionsSourceSnapshot snapshot, ClientSanctionsBatch batch,
        ParsedSanctionsList list, SanctionsSourceCheck check, CancellationToken ct)
    {
        var profiles = await db.EmployeeProfiles.Where(x => x.EmploymentStatus != "Inactive").ToListAsync(ct);
        var recipients = await RecipientsAsync(db);
        foreach (var profile in profiles)
        {
            var scope = EmployeeScope(profile);
            var hash = SanctionsList.ScopeHash(scope);
            var previous = await db.SanctionsAutomatedResults.Where(x => x.SanctionsSourceSnapshotId == snapshot.Id && x.EmployeeProfileId == profile.Id)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (previous?.ScopeHash == hash) continue;
            var key = $"auto-tfs:{snapshot.Id}:{hash}";
            var task = await db.EmployeeComplianceTasks.SingleOrDefaultAsync(x => x.EmployeeProfileId == profile.Id && x.TriggerKey == key, ct);
            if (task is null)
            {
                task = new() { EmployeeProfileId = profile.Id, TriggerKey = key, Kind = "TfsListUpdate",
                    EmployeeTfsBatchId = snapshot.EmployeeTfsBatchId, RecipientUserIdsJson = JsonSerializer.Serialize(recipients),
                    Reason = "Automatic official list check; no employee review approval is inferred." };
                db.EmployeeComplianceTasks.Add(task);
            }
            var match = SanctionsList.Match(list, scope);
            var lastConcern = await db.SanctionsAutomatedResults.Where(x => x.EmployeeProfileId == profile.Id && x.Outcome != "NoMatch")
                .OrderByDescending(x => x.PerformedAtUtc).FirstOrDefaultAsync(ct);
            var manuallyResolved = lastConcern is not null && await (from c in db.EmployeeComplianceChecks
                join r in db.EmployeeComplianceReviews on c.EmployeeComplianceReviewId equals r.Id
                where r.EmployeeProfileId == profile.Id && c.Kind == "TFS" && (c.Outcome == "NoMatch" || c.Outcome == "FalsePositive") &&
                    c.PerformedAtUtc >= lastConcern.PerformedAtUtc && c.SourceUrl == snapshot.SourceUrl select c).AnyAsync(ct);
            var concern = await (from c in db.EmployeeComplianceChecks join r in db.EmployeeComplianceReviews on c.EmployeeComplianceReviewId equals r.Id
                where r.EmployeeProfileId == profile.Id && c.Kind == "TFS" &&
                    (c.Outcome == "ConfirmedDesignation" || c.Outcome == "Concern" || c.Outcome == "Unresolved" || c.Outcome == "SourceFailed") &&
                    !db.EmployeeComplianceChecks.Any(n => n.SupersedesCheckId == c.Id) select c).AnyAsync(ct);
            if (match.Outcome == "NoMatch" && (concern || lastConcern is not null && !manuallyResolved))
                match = new("ManualReviewRequired", "An earlier employee TFS concern requires explicit human resolution; automation does not supersede it.", []);
            var result = new SanctionsAutomatedResult { SanctionsSourceSnapshotId = snapshot.Id, EmployeeProfileId = profile.Id,
                ScopeHash = hash, ScopeJson = JsonSerializer.Serialize(scope), Outcome = match.Outcome, CandidatesJson = JsonSerializer.Serialize(match.Candidates),
                Finding = match.Finding + " " + Limitations, PerformedAtUtc = ComplianceWorkflowAccess.ActualTime(DateTime.UtcNow) };
            db.SanctionsAutomatedResults.Add(result);
            task.Reason = result.Finding;
            task.Status = match.Outcome == "NoMatch" ? "Closed" : "Open";
            task.ClosedAtUtc = match.Outcome == "NoMatch" ? DateTime.UtcNow : null;
            // Identity changes supersede old scope tasks, not their evidence or any adverse finding.
            var oldTasks = await db.EmployeeComplianceTasks.Where(x => x.EmployeeProfileId == profile.Id &&
                x.TriggerKey.StartsWith("auto-tfs:") && x.TriggerKey != key && x.Status != "Closed").ToListAsync(ct);
            if (match.Outcome == "NoMatch")
                foreach (var old in oldTasks) { old.Status = "Closed"; old.ClosedAtUtc = DateTime.UtcNow; old.Reason += " Subsequent supported coverage; retained in history."; }
            db.EmployeeComplianceAuditEvents.Add(new() { EmployeeProfileId = profile.Id, Action = "AutomatedTfsResult", UserId = SystemUserId,
                Reason = result.Finding, SnapshotJson = JsonSerializer.Serialize(new { hash, result.Outcome, snapshot.Id, match.Candidates }) });
            check.SubjectsChecked++;
            if (match.Outcome == "NoMatch") check.NoCandidates++; else check.NeedsReview++;
        }
    }

    internal static SanctionsNameScope ClientScope(ClientSanctionsSubject subject)
    {
        using var json = JsonDocument.Parse(subject.IdentitySummary);
        var value = json.RootElement;
        string Get(string field) => value.TryGetProperty(field, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
        var entity = subject.SubjectKey.StartsWith("C:") ? Get("ClientCategory") != ClientCategories.NaturalPerson : Get("PartyType") == ClientRelatedPartyTypes.LegalEntity;
        var fullName = Get("FullName");
        var surname = Get("SurnameOrEntityName");
        var names = new List<string> { subject.SubjectName };
        if (fullName.Length > 0 && surname.Length > 0) names.Add(fullName.Contains(surname, StringComparison.OrdinalIgnoreCase) ? fullName : fullName + " " + surname);
        var identifiers = new[] { Get("SouthAfricanIdNumber"), Get("PassportNumber"), Get("RegistrationNumber") }.Where(x => x.Length > 0).ToList();
        return new(names.Distinct().ToList(), identifiers, entity, Get("PartyType") == ClientRelatedPartyTypes.BeneficiaryClass ||
            !entity && Regex.IsMatch(subject.SubjectName, @"(?:\s(?:and|en)\s|&)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    internal static SanctionsNameScope EmployeeScope(EmployeeProfile profile) => new(
        new[] { profile.LegalName, profile.DisplayName }.Concat(profile.Aliases.Split([';', '\n', ','], StringSplitOptions.RemoveEmptyEntries)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
        Regex.Matches(profile.IdentityReference, @"\b\d{13}\b").Select(x => x.Value).Distinct().ToList());

    internal static async Task<bool> EmployeeClearAsync(ApplicationDbContext db, EmployeeProfile profile)
    {
        var snapshot = await db.SanctionsSourceSnapshots.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (snapshot is null) return false;
        var latestBatch = await db.EmployeeTfsBatches.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        if (latestBatch?.Id != snapshot.EmployeeTfsBatchId) return false;
        var result = await db.SanctionsAutomatedResults.Where(x => x.EmployeeProfileId == profile.Id && x.SanctionsSourceSnapshotId == snapshot.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        return result?.Outcome == "NoMatch" && result.ScopeHash == SanctionsList.ScopeHash(EmployeeScope(profile)) &&
            await SourceBlockerAsync(db, null) is null;
    }

    internal static async Task<string?> SourceBlockerAsync(ApplicationDbContext db, ClientSanctionsBatch? batch)
    {
        var latest = await db.SanctionsSourceChecks.AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (latest is null) return null;
        // A later, actually recorded manual official-source batch remains a supported alternative.
        if (batch is not null && batch.CreatedBy != Performer && batch.CreatedAtUtc > latest.CompletedAtUtc) return null;
        if (latest.Outcome == "Failed") return "Official sanctions source retrieval/validation failed; obtain a supported current check before proceeding.";
        if (latest.CompletedAtUtc < DateTime.UtcNow.AddHours(-latest.MaximumSourceAgeHours)) return "Official sanctions source refresh is overdue; obtain a supported current check before proceeding.";
        return null;
    }

    private async Task<SanctionsSourceCheck> RecordFailureAsync(DateTime start, string detail, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var check = new SanctionsSourceCheck { StartedAtUtc = start, CompletedAtUtc = DateTime.UtcNow, SourceUrl = options.Value.SourceUrl,
            MaximumSourceAgeHours = options.Value.MaximumSourceAgeHours, Outcome = "Failed", Detail = detail };
        db.SanctionsSourceChecks.Add(check); await db.SaveChangesAsync(ct);
        if (!await db.ComplianceTasks.AnyAsync(x => x.LinkedEntityType == nameof(SanctionsSourceCheck) && x.Status != ComplianceStatuses.Closed, ct))
            db.ComplianceTasks.Add(new() { TaskType = ComplianceTaskTypes.SanctionsCoverage, Title = "Official sanctions source unavailable or incomplete",
                Owner = ComplianceWorkService.ComplianceReviewAudience, Priority = "High", Status = ComplianceWorkStatuses.Open,
                LinkedEntityType = nameof(SanctionsSourceCheck), LinkedEntityId = check.Id, Description = detail + " No clear results or replacement snapshots were saved.", UpdatedBy = Performer });
        Audit(db, nameof(SanctionsSourceCheck), check.Id, "OfficialSourceFailed", new { detail });
        await db.SaveChangesAsync(ct);
        return check;
    }

    internal static Task<List<string>> RecipientsAsync(ApplicationDbContext db) => (from user in db.Users where user.IsApproved
        join link in db.UserRoles on user.Id equals link.UserId join role in db.Roles on link.RoleId equals role.Id
        where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover select user.Id).Distinct().OrderBy(x => x).ToListAsync();

    private static void Audit(ApplicationDbContext db, string entity, int id, string action, object value) => db.ComplianceAuditEvents.Add(new()
        { EntityType = entity, EntityId = id, Action = action, UserName = Performer, Reason = "Actual official-list automation event; no human approval inferred.", NewValueJson = JsonSerializer.Serialize(value) });
}
