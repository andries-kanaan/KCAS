using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using static KCAS.Admin.Data.ComplianceWorkflowAccess;

namespace KCAS.Admin.Data;

public sealed class ClientSanctionsCoverageService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<ComplianceControlReminder?> ReminderAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        if (!await ReceivesReviewsAsync(db, actor.Id)) return null;
        var batch = await db.ClientSanctionsBatches.AsNoTracking().OrderByDescending(x => x.SourcePublishedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        if (batch is null) return null;
        var admin = await IsAdminAsync(db, actor.Id);
        var rows = await RowsAsync(db, batch, admin);
        var remaining = rows.Count(x => x.Status is not ("Clear" or "Excluded"));
        var current = await PopulationAsync(db);
        var visibleIds = await db.Clients.Where(x => admin || !x.ExcludeFromComplianceLists).Select(x => x.Id).ToListAsync();
        remaining += current.Count(x => visibleIds.Contains(x.ClientId) && !rows.Any(r => r.Subject.SubjectKey == x.SubjectKey && r.Subject.ScopeHash == x.ScopeHash));
        return remaining == 0 ? null : new("TFS coverage: " + batch.SourceVersion, remaining, $"/compliance/sanctions/{batch.Id}");
    }
    public async Task<IReadOnlyList<SanctionsBatchRow>> RegisterAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var admin = await IsAdminAsync(db, actor.Id);
        var rows = new List<SanctionsBatchRow>();
        foreach (var batch in await db.ClientSanctionsBatches.AsNoTracking().OrderByDescending(x => x.SourcePublishedAtUtc).ThenByDescending(x => x.Id).ToListAsync())
        {
            var subjects = await RowsAsync(db, batch, admin);
            rows.Add(new(batch, subjects.Count, subjects.Count(x => x.Status == "Clear"), subjects.Count(x => x.Status == "Excluded"),
                subjects.Count(x => x.Status is not ("Clear" or "Excluded")), subjects.Count(x => x.Status is "PossibleMatch" or "ConfirmedMatch")));
        }
        return rows;
    }

    public async Task<int> CreateAsync(SanctionsBatchEdit edit, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var version = Required(edit.SourceVersion, "Official source/list version", 191);
            var url = OfficialUrl(edit.SourceUrl);
            var date = ActualTime(edit.SourcePublishedAtUtc);
            var reason = Required(edit.Reason, "Source update reason");
            var existing = await db.ClientSanctionsBatches.SingleOrDefaultAsync(x => x.SourceVersion == version);
            if (existing is not null)
            {
                if (existing.SourceUrl != url || existing.SourcePublishedAtUtc != date) throw new ValidationException("This version is already recorded with a different source or publication time.");
                return existing.Id;
            }
            var recipients = await (from user in db.Users where user.IsApproved
                join link in db.UserRoles on user.Id equals link.UserId join role in db.Roles on link.RoleId equals role.Id
                where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover
                select user.Id).Distinct().OrderBy(x => x).ToListAsync();
            if (recipients.Count == 0) throw new ValidationException("No approved Compliance Administrator or Approver is available for the in-app tasks.");
            var batch = new ClientSanctionsBatch { SourceVersion = version, SourceUrl = url, SourcePublishedAtUtc = date, Reason = reason,
                CreatedBy = Label(actor), RecipientUserIdsJson = JsonSerializer.Serialize(recipients) };
            db.ClientSanctionsBatches.Add(batch); await db.SaveChangesAsync();
            await SynchronizeAsync(db, batch, actor, reason);
            Audit(db, nameof(ClientSanctionsBatch), batch.Id, "ListUpdateRecorded", actor, reason, new { version, url, date, recipients });
            return batch.Id;
        });

    public async Task<SanctionsBatchPage> LoadAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceView);
        var batch = await db.ClientSanctionsBatches.AsNoTracking().SingleAsync(x => x.Id == id);
        var admin = await IsAdminAsync(db, actor.Id);
        var rows = await RowsAsync(db, batch, admin);
        var current = await PopulationAsync(db);
        var stored = await db.ClientSanctionsSubjects.AsNoTracking().Where(x => x.ClientSanctionsBatchId == id && x.IsCurrent).ToListAsync();
        var history = await db.ClientSanctionsCoverageRecords.AsNoTracking().Include(x => x.Subject).ThenInclude(x => x.Client).Include(x => x.Evidence)
            .Where(x => x.Subject.ClientSanctionsBatchId == id && (admin || !x.Subject.Client.ExcludeFromComplianceLists))
            .OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).ToListAsync();
        var changed = !current.Select(x => x.SubjectKey + x.ScopeHash).Order().SequenceEqual(stored.Select(x => x.SubjectKey + x.ScopeHash).Order());
        return new(batch, rows, history, changed, await PermissionAsync(db, actor.Id, KcasPermissions.ComplianceManage), admin,
            $"Official TFS list: {batch.SourceVersion}; source: {batch.SourceUrl}; published: {batch.SourcePublishedAtUtc:O}.\n" +
            "Read actual identity and party evidence; screen every outstanding subject against this exact official list version. " +
            "Record actual findings through the existing client evidence service with source URL/version, actual UTC time, Codex performer and authenticated saving user. " +
            "Do not scan folders, clear failed sources, infer identity matches or manufacture KI/MLCO decisions. " +
            "Retain possible/confirmed matches and request the required escalation. Link the resulting evidence to each subject in this coverage batch.\n" +
            string.Join("\n", rows.Where(x => x.Status is not ("Clear" or "Excluded")).Select(x =>
                $"Client: {x.Subject.Client.DisplayName}; KCAS ID: {x.Subject.ClientId}; Kanaan ID: {x.Subject.Client.KanaanId}; " +
                $"folder: {x.Subject.Client.ClientFolder}; subject: {x.Subject.SubjectName} ({x.Subject.SubjectType}); " +
                $"party ID: {x.Subject.ClientRelatedPartyId}; identifiers: {x.Subject.IdentitySummary}; coverage row: {x.Subject.Id}; scope: {x.Subject.ScopeHash}; status: {x.Status}.")));
    }

    public async Task RefreshPopulationAsync(int id, string version, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var batch = await db.ClientSanctionsBatches.SingleAsync(x => x.Id == id); Expect(batch.Version, version);
            await SynchronizeAsync(db, batch, actor, Required(reason, "Population change reason")); return true;
        });

    public async Task RecordEvidenceAsync(int subjectId, int evidenceId, string version, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            var subject = await SubjectAsync(db, subjectId, version, actor);
            var evidence = await db.ClientEvidenceItems.SingleAsync(x => x.Id == evidenceId);
            var changedScope = await db.ClientSanctionsSubjects.AnyAsync(x => x.ClientSanctionsBatchId == subject.ClientSanctionsBatchId && x.SubjectKey == subject.SubjectKey && x.ScopeHash != subject.ScopeHash);
            var reset = await db.ClientSanctionsCoverageRecords.Where(x => x.ClientSanctionsSubjectId == subject.Id && x.Outcome == "ScopeChanged").MaxAsync(x => (DateTime?)x.RecordedAtUtc);
            var after = reset ?? (changedScope ? subject.CreatedAtUtc : subject.Batch.SourcePublishedAtUtc);
            if (!Supports(subject, evidence, after)) throw new ValidationException("Use a verified, selected screening for this subject, official URL/list version and actual check time. Old, superseded, failed or differently scoped evidence cannot complete coverage.");
            if (evidence.ScreeningOutcome == ClientEvidenceScreeningOutcomes.NoMatch &&
                await HasDesignationAsync(db, subject.ClientId, subject.SubjectKey))
                throw new ValidationException("A recorded confirmed designation cannot be cleared by an ordinary no-match check or exclusion; follow the MLCO/legal resolution process.");
            var record = new ClientSanctionsCoverageRecord { ClientSanctionsSubjectId = subject.Id, ClientEvidenceItemId = evidenceId,
                EvidenceFingerprint = Fingerprint(evidence), Outcome = evidence.ScreeningOutcome!, Reason = Required(reason, "Finding/coverage reason"), RecordedBy = Label(actor) };
            db.ClientSanctionsCoverageRecords.Add(record);
            await UpdateTaskAsync(db, subject, record.Outcome == ClientEvidenceScreeningOutcomes.NoMatch, record.Reason);
            subject.Batch.Version = NewVersion();
            Audit(db, nameof(ClientSanctionsSubject), subject.Id, "ScreeningEvidenceLinked", actor, record.Reason,
                new { evidenceId, record.Outcome, record.EvidenceFingerprint, evidence.ScreeningPerformedBy, evidence.ScreeningReviewedAtUtc });
            return true;
        });

    public async Task ExcludeAsync(int subjectId, string version, string basis, string reference, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            if (!await PermissionAsync(db, actor.Id, KcasPermissions.ComplianceApprove)) throw new UnauthorizedAccessException("Current compliance-approval permission is required for a population exclusion.");
            var subject = await SubjectAsync(db, subjectId, version, actor);
            if (basis is not ("RelationshipEnded" or "VerifiedDuplicate" or "NotApplicableParty")) throw new ValidationException("Use a supported exclusion basis.");
            if (basis == "NotApplicableParty" && subject.SubjectType == ClientEvidenceScreeningSubjectTypes.Client)
                throw new ValidationException("A client cannot be excluded as a non-applicable party.");
            if (await HasDesignationAsync(db, subject.ClientId, subject.SubjectKey)) throw new ValidationException("An exclusion cannot bypass a confirmed designation.");
            if ((await RowsAsync(db, subject.Batch, true, subject.ClientId)).Any(x => x.Subject.Id == subject.Id && x.Status is "PossibleMatch" or "ConfirmedMatch" or "EscalationRequired"))
                throw new ValidationException("Resolve the screening concern before recording an ordinary scope exclusion.");
            var record = new ClientSanctionsCoverageRecord { ClientSanctionsSubjectId = subject.Id, Outcome = "Excluded",
                ExclusionReference = Required(reference, "Actual termination, canonical duplicate coverage or party-scope evidence"),
                Reason = basis + ": " + Required(reason, "Exclusion reason"), RecordedBy = Label(actor) };
            db.ClientSanctionsCoverageRecords.Add(record); await UpdateTaskAsync(db, subject, true, record.Reason);
            subject.Batch.Version = NewVersion();
            Audit(db, nameof(ClientSanctionsSubject), subject.Id, "SupportedScopeExclusion", actor, record.Reason, new { record.ExclusionReference, basis });
            return true;
        });

    public static async Task<List<string>> BlockersAsync(ApplicationDbContext db, int clientId)
    {
        var batch = await db.ClientSanctionsBatches.AsNoTracking().OrderByDescending(x => x.SourcePublishedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        if (batch is null)
            return await RawDesignationAsync(db, clientId) ? ["A confirmed sanctions designation requires MLCO/legal resolution."] : [];
        var expected = await PopulationAsync(db, clientId);
        var rows = await RowsAsync(db, batch, true, clientId);
        var blockers = new List<string>();
        foreach (var subject in expected)
        {
            var row = rows.SingleOrDefault(x => x.Subject.SubjectKey == subject.SubjectKey && x.Subject.ScopeHash == subject.ScopeHash);
            if (row is null || row.Status is not ("Clear" or "Excluded")) blockers.Add($"TFS list {batch.SourceVersion}: {subject.SubjectName} requires current supported coverage{(row is null ? " (refresh the population)" : " (" + row.Status + ")")}.");
        }
        if (await RawDesignationAsync(db, clientId) || await db.ClientSanctionsCoverageRecords.AnyAsync(x => x.Subject.ClientId == clientId && x.Outcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch))
            blockers.Add("A confirmed sanctions designation remains escalated; ordinary coverage cannot override it.");
        return blockers;
    }

    private static async Task<ClientSanctionsSubject> SubjectAsync(ApplicationDbContext db, int id, string version, ApplicationUser actor)
    {
        var subject = await db.ClientSanctionsSubjects.Include(x => x.Batch).Include(x => x.Client).SingleAsync(x => x.Id == id);
        await VisibleAsync(db, subject.Client, actor.Id); Expect(subject.Batch.Version, version);
        if (!subject.IsCurrent || !(await PopulationAsync(db, subject.ClientId)).Any(x => x.SubjectKey == subject.SubjectKey && x.ScopeHash == subject.ScopeHash))
            throw new ValidationException("The subject/identity scope changed. Refresh the batch and use the current row.");
        return subject;
    }
    private static async Task SynchronizeAsync(ApplicationDbContext db, ClientSanctionsBatch batch, ApplicationUser actor, string reason)
    {
        var current = await PopulationAsync(db);
        var stored = await db.ClientSanctionsSubjects.Where(x => x.ClientSanctionsBatchId == batch.Id).ToListAsync();
        var keys = current.Select(x => x.SubjectKey + x.ScopeHash).ToHashSet();
        foreach (var old in stored.Where(x => x.IsCurrent && !keys.Contains(x.SubjectKey + x.ScopeHash)))
        {
            old.IsCurrent = false; await UpdateTaskAsync(db, old, true, "Scope superseded; retained in history.");
        }
        foreach (var scope in current)
        {
            var subject = stored.SingleOrDefault(x => x.SubjectKey == scope.SubjectKey && x.ScopeHash == scope.ScopeHash);
            if (subject is not null)
            {
                if (!subject.IsCurrent)
                {
                    db.ClientSanctionsCoverageRecords.Add(new() { ClientSanctionsSubjectId = subject.Id, Outcome = "ScopeChanged",
                        Reason = "Identity scope restored after a change; obtain a fresh check.", RecordedBy = Label(actor) });
                    await UpdateTaskAsync(db, subject, false, "Restored scope requires a fresh check.");
                }
                subject.IsCurrent = true; continue;
            }
            scope.ClientSanctionsBatchId = batch.Id;
            scope.Task = new ComplianceTask { ClientId = scope.ClientId, TaskType = ComplianceTaskTypes.SanctionsCoverage,
                Title = $"TFS list-update check: {scope.SubjectName[..Math.Min(scope.SubjectName.Length, 210)]}", Status = ComplianceWorkStatuses.Open, Owner = ComplianceWorkService.ComplianceReviewAudience,
                Priority = "High", LinkedEntityType = nameof(ClientSanctionsBatch), LinkedEntityId = batch.Id,
                Description = $"Codex/manual check required against {batch.SourceVersion}, {batch.SourceUrl}. Scope {scope.ScopeHash}. Creation is not screening.", UpdatedBy = Label(actor) };
            db.ClientSanctionsSubjects.Add(scope);
        }
        batch.Version = NewVersion();
        Audit(db, nameof(ClientSanctionsBatch), batch.Id, "PopulationCaptured", actor, reason, new { Subjects = current.Count, Clients = current.Select(x => x.ClientId).Distinct().Count() });
    }

    private static async Task<List<ClientSanctionsSubject>> PopulationAsync(ApplicationDbContext db, int? clientId = null)
    {
        var clients = await db.Clients.AsNoTracking().Include(x => x.PersonalProfile).Include(x => x.Relationships)
            .Include(x => x.RelatedParties).ThenInclude(x => x.Roles).AsSplitQuery().Where(x => clientId == null || x.Id == clientId).ToListAsync();
        var result = new List<ClientSanctionsSubject>();
        foreach (var client in clients)
        {
            Add(client, $"C:{client.Id}", ClientEvidenceScreeningSubjectTypes.Client, client.DisplayName, null,
                JsonSerializer.Serialize(new { client.FullName, client.SurnameOrEntityName, client.ClientCategory, client.PersonalProfile?.SouthAfricanIdNumber }));
            foreach (var party in client.RelatedParties.Where(x => x.IsActive))
                Add(client, $"P:{party.Id}", ClientEvidenceReadinessService.MapRelatedPartySubjectType(party.Roles.Select(x => x.RoleCode)), party.DisplayName, party.Id,
                    JsonSerializer.Serialize(new { party.PartyType, party.SouthAfricanIdNumber, party.PassportNumber, party.PassportCountry, party.BirthDate, party.RegistrationNumber,
                        party.Nationality, Roles = party.Roles.Select(x => x.RoleCode).Order() }));
            foreach (var relationship in client.Relationships.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                Add(client, $"R:{relationship.Id}", ClientEvidenceReadinessService.MapRelationshipSubjectType(relationship.RelationshipType), relationship.Name!, null,
                    JsonSerializer.Serialize(new { relationship.RelationshipType, relationship.BirthDate }));
        }
        return result;
        void Add(Client client, string key, string type, string name, int? partyId, string identifiers)
        {
            result.Add(new() { ClientId = client.Id, SubjectKey = key, SubjectType = type, SubjectName = name.Trim(), ClientRelatedPartyId = partyId,
                IdentitySummary = identifiers, ScopeHash = Hash(key + "|" + type + "|" + name.Trim() + "|" + identifiers) });
        }
    }

    private static async Task<List<SanctionsSubjectRow>> RowsAsync(ApplicationDbContext db, ClientSanctionsBatch batch, bool admin, int? clientId = null)
    {
        var subjects = await db.ClientSanctionsSubjects.AsNoTracking().Include(x => x.Client).Include(x => x.Batch)
            .Where(x => x.ClientSanctionsBatchId == batch.Id && x.IsCurrent && (clientId == null || x.ClientId == clientId) && (admin || !x.Client.ExcludeFromComplianceLists)).ToListAsync();
        var records = await db.ClientSanctionsCoverageRecords.AsNoTracking().Include(x => x.Evidence)
            .Where(x => x.Subject.ClientSanctionsBatchId == batch.Id).OrderByDescending(x => x.Id).ToListAsync();
        var clientIds = subjects.Select(s => s.ClientId).Distinct().ToList();
        var history = await db.ClientSanctionsSubjects.AsNoTracking().Where(x => x.ClientSanctionsBatchId == batch.Id && clientIds.Contains(x.ClientId))
            .Select(x => new { x.SubjectKey, x.ScopeHash }).ToListAsync();
        var changedKeys = history.GroupBy(x => x.SubjectKey).Where(x => x.Select(s => s.ScopeHash).Distinct().Count() > 1).Select(x => x.Key).ToHashSet();
        var evidence = await db.ClientEvidenceItems.AsNoTracking().Where(x => x.EvidenceType == "SanctionsTfs" && clientIds.Contains(x.ClientId)).ToListAsync();
        var expected = (await PopulationAsync(db, clientId)).ToDictionary(x => x.SubjectKey, x => x.ScopeHash);
        return subjects.OrderBy(x => x.Client.DisplayName).ThenBy(x => x.SubjectName).Select(subject =>
        {
            var record = records.FirstOrDefault(x => x.ClientSanctionsSubjectId == subject.Id);
            var reset = records.Where(x => x.ClientSanctionsSubjectId == subject.Id && x.Outcome == "ScopeChanged").Select(x => (DateTime?)x.RecordedAtUtc).Max();
            var after = reset ?? (changedKeys.Contains(subject.SubjectKey) ? subject.CreatedAtUtc : batch.SourcePublishedAtUtc);
            var concern = evidence.Where(x => SameSubject(subject, x) && x.Status == ClientEvidenceStatuses.Verified &&
                ClientEvidenceOwnershipStatuses.IsActive(x.OwnershipStatus) && x.SelectionStatus == ClientEvidenceSelectionStatuses.Current &&
                x.SupersededByClientEvidenceItemId == null &&
                (x.ScreeningOutcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch ||
                 (record?.Evidence?.ScreeningReviewedAtUtc is null || x.ScreeningReviewedAtUtc >= record.Evidence.ScreeningReviewedAtUtc) &&
                 (x.ScreeningOutcome == ClientEvidenceScreeningOutcomes.PossibleMatch || x.EscalationRequired)))
                .OrderByDescending(x => x.ScreeningOutcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch).ThenByDescending(x => x.ScreeningReviewedAtUtc).FirstOrDefault();
            var status = !expected.TryGetValue(subject.SubjectKey, out var scope) || scope != subject.ScopeHash ? "ScopeChanged" :
                concern is not null ? (concern.EscalationRequired && concern.ScreeningOutcome == ClientEvidenceScreeningOutcomes.NoMatch ? "EscalationRequired" : concern.ScreeningOutcome ?? "EscalationRequired") :
                record is null ? "Outstanding" : record.Outcome == "Excluded" ? "Excluded" :
                record.Evidence is null || record.EvidenceFingerprint != Fingerprint(record.Evidence) || !Supports(subject, record.Evidence, after) ? "EvidenceChanged" :
                record.Outcome == ClientEvidenceScreeningOutcomes.NoMatch ? "Clear" : record.Outcome;
            return new SanctionsSubjectRow(subject, status, record, evidence.Where(x => Supports(subject, x, after)).OrderByDescending(x => x.ScreeningReviewedAtUtc).ToList());
        }).ToList();
    }
    private static bool Supports(ClientSanctionsSubject s, ClientEvidenceItem e, DateTime after) => e.ClientId == s.ClientId && e.EvidenceType == "SanctionsTfs" &&
        e.Status == ClientEvidenceStatuses.Verified && e.SelectionStatus == ClientEvidenceSelectionStatuses.Current &&
        ClientEvidenceOwnershipStatuses.IsActive(e.OwnershipStatus) && e.SupersededByClientEvidenceItemId == null &&
        SameSubject(s, e) && e.ScreeningReviewedAtUtc >= after && e.ScreeningReviewedAtUtc <= DateTime.UtcNow.AddMinutes(1) &&
        (e.ScreeningOutcome != ClientEvidenceScreeningOutcomes.NoMatch || !e.EscalationRequired) &&
        (e.ExpiryDate == null || e.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today)) &&
        !string.IsNullOrWhiteSpace(e.ScreeningPerformedBy) && !string.IsNullOrWhiteSpace(e.Reviewer) && !string.IsNullOrWhiteSpace(e.Notes) &&
        HasReference(e.ScreeningSources, s.Batch.SourceUrl, true) && HasReference(e.ScreeningSources, s.Batch.SourceVersion, false) &&
        e.ScreeningOutcome is ClientEvidenceScreeningOutcomes.NoMatch or ClientEvidenceScreeningOutcomes.PossibleMatch or ClientEvidenceScreeningOutcomes.ConfirmedMatch;
    private static bool HasReference(string? sources, string reference, bool url)
    {
        if (string.IsNullOrWhiteSpace(sources) || string.IsNullOrWhiteSpace(reference)) return false;
        var boundary = url ? @"[^\s<>""'\[\](){};,]" : @"[\p{L}\p{N}_]";
        return Regex.IsMatch(sources, "(?<!" + boundary + ")" + Regex.Escape(reference) + "(?!" + boundary + ")",
            url ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }
    private static string Fingerprint(ClientEvidenceItem e) => Hash(JsonSerializer.Serialize(new { e.Id, e.ClientId, e.ClientRelatedPartyId, e.Status, e.OwnershipStatus, e.SelectionStatus,
        e.SupersededByClientEvidenceItemId, e.ScreeningSubjectName, e.ScreeningSubjectType, e.ScreeningSources, e.ScreeningOutcome, e.ScreeningPerformedBy, e.Reviewer,
        ReviewedAt = e.ScreeningReviewedAtUtc?.Ticks / 10, e.ExpiryDate, e.Notes, e.EscalationRequired, e.ScreeningRiskSignal,
        e.SourcePath, e.RelativePath, e.FileSha256, e.FileSizeBytes }));
    private static bool SameSubject(ClientSanctionsSubject s, ClientEvidenceItem e) => e.ClientId == s.ClientId &&
        e.ScreeningSubjectType == s.SubjectType && string.Equals(e.ScreeningSubjectName?.Trim(), s.SubjectName, StringComparison.OrdinalIgnoreCase) &&
        e.ClientRelatedPartyId == s.ClientRelatedPartyId;
    private static async Task<bool> HasDesignationAsync(ApplicationDbContext db, int clientId, string key) =>
        await RawDesignationAsync(db, clientId) || await db.ClientSanctionsCoverageRecords
            .AnyAsync(x => x.Subject.ClientId == clientId && x.Subject.SubjectKey == key && x.Outcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch);
    private static Task<bool> RawDesignationAsync(ApplicationDbContext db, int clientId) => db.ClientEvidenceItems.AnyAsync(x =>
        x.ClientId == clientId && x.EvidenceType == "SanctionsTfs" && x.Status == ClientEvidenceStatuses.Verified &&
        (x.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed || x.OwnershipStatus == ClientEvidenceOwnershipStatuses.AutoAssigned) &&
        x.ScreeningOutcome == ClientEvidenceScreeningOutcomes.ConfirmedMatch);
    private static async Task UpdateTaskAsync(ApplicationDbContext db, ClientSanctionsSubject s, bool satisfied, string reason)
    {
        var task = s.Task ?? (s.ComplianceTaskId.HasValue ? await db.ComplianceTasks.SingleAsync(x => x.Id == s.ComplianceTaskId) : null);
        if (task is null) return;
        task.Status = satisfied ? ComplianceStatuses.Closed : ComplianceWorkStatuses.Open; task.ClosureReason = satisfied ? reason : null;
        task.ClosedAtUtc = satisfied ? DateTime.UtcNow : null;
    }
    private async Task<T> WriteAsync<T>(ClaimsPrincipal principal, Func<ApplicationDbContext, ApplicationUser, Task<T>> action)
    {
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceManage); var result = await action(db, actor);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ValidationException("The coverage batch changed. Reload before saving."); }
        return result;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string OfficialUrl(string value)
    {
        value = Required(value, "Official source URL", 1024);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo != "" ||
            !(url.Host == "fic.gov.za" || url.Host.EndsWith(".fic.gov.za", StringComparison.OrdinalIgnoreCase) || url.Host == "un.org" || url.Host.EndsWith(".un.org", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Use an actual official HTTPS FIC or UN TFS source URL without credentials.");
        return value;
    }
}

public sealed class SanctionsBatchEdit
{
    public string SourceVersion { get; set; } = "";
    public string SourceUrl { get; set; } = "https://tfs.fic.gov.za/";
    public DateTime SourcePublishedAtUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";
}
public sealed record SanctionsBatchRow(ClientSanctionsBatch Batch, int Total, int Clear, int Excluded, int Remaining, int Matches);
public sealed record ComplianceControlReminder(string Title, int Outstanding, string Url);
public sealed record SanctionsSubjectRow(ClientSanctionsSubject Subject, string Status, ClientSanctionsCoverageRecord? Record, IReadOnlyList<ClientEvidenceItem> AvailableEvidence);
public sealed record SanctionsBatchPage(ClientSanctionsBatch Batch, IReadOnlyList<SanctionsSubjectRow> Subjects, IReadOnlyList<ClientSanctionsCoverageRecord> History, bool PopulationChanged, bool CanManage, bool IsAdministrator, string CodexBrief);
