using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOnboardingService(IDbContextFactory<ApplicationDbContext> factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions FingerprintJson = new(JsonSerializerDefaults.Web)
        { Converters = { new DatabaseTimestampConverter() } };

    public async Task<ClientOnboardingModel> LoadAsync(int clientId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal);
        var visibleClient = await db.Clients.AsNoTracking().SingleAsync(x => x.Id == clientId);
        await VisibleAsync(db, visibleClient, actor.Id);
        var model = await BuildAsync(db, clientId);
        await VisibleAsync(db, model.Client, actor.Id);
        model.CanPrepare = await CanPrepareAsync(db, actor.Id);
        model.CanDecide = await KiAsync(db, actor) is not null;
        return model;
    }

    public async Task SavePreparationAsync(int clientId, ClientOnboardingEdit edit, string reason, ClaimsPrincipal principal)
        => await WriteAsync(clientId, principal, async (db, actor, model) =>
        {
            await RequirePrepareAsync(db, actor.Id);
            if (model.IsAccepted) throw new ValidationException("Start a new acceptance review before changing the accepted preparation.");
            var profile = model.Profile ?? new ClientOnboardingProfile { ClientId = clientId };
            if (model.Profile is not null && edit.Version != profile.Version) throw new ValidationException("Preparation changed. Reload before saving.");
            if (model.Profile is null) db.ClientOnboardingProfiles.Add(profile);
            profile.RequestedService = Required(edit.RequestedService, "Requested service");
            profile.ResponsibleRepresentative = Required(edit.ResponsibleRepresentative, "Responsible representative");
            profile.PurposeAndProposedFunds = Required(edit.PurposeAndProposedFunds, "Purpose and proposed funds");
            profile.DisclosureVersion = edit.DisclosureVersion.Trim();
            profile.DisclosureDeliveryReference = edit.DisclosureDeliveryReference.Trim();
            profile.EnhancedMeasures = edit.EnhancedMeasures.Trim();
            if (edit.DisclosureDeliveredAtUtc > DateTime.UtcNow.AddMinutes(1)) throw new ValidationException("Use the actual disclosure-delivery time, not a future time.");
            profile.DisclosureDeliveredAtUtc = edit.DisclosureDeliveredAtUtc;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            profile.UpdatedBy = actor.Email ?? actor.UserName ?? actor.Id;
            profile.ImportSourceReference = null;
            profile.Version = Guid.NewGuid().ToString("N");
            model.Client.RequiresClientAcceptance = true;
            Audit(db, clientId, "PreparationRecorded", actor, Required(reason, "Reason"), new { profile.RequestedService, profile.Version });
        });

    public async Task RequestCodexAsync(int clientId, string reason, ClaimsPrincipal principal)
        => await WriteAsync(clientId, principal, async (db, actor, model) =>
        {
            await RequirePrepareAsync(db, actor.Id);
            model.Client.RequiresClientAcceptance = true;
            if (model.IsAccepted) throw new ValidationException("The current accepted review does not need a duplicate Codex check.");
            if (model.Request is { Status: "AwaitingCodex" } existing && existing.MaterialHash == model.MaterialHash)
            {
                var currentBrief = Brief(model);
                if (existing.Brief != currentBrief)
                {
                    existing.Brief = currentBrief;
                    existing.Task.Description = currentBrief;
                    Audit(db, clientId, "CodexBriefRefreshed", actor, "Refresh the outstanding preparation and check gaps for the same review scope.", new { model.MaterialHash });
                }
                return;
            }
            if (model.Request is { Status: "AwaitingCodex" } previous)
            {
                previous.Status = "Superseded";
                previous.Task.Status = ComplianceStatuses.Withdrawn;
                previous.Task.ClosureReason = "Material scope changed; superseded by a new Codex request.";
            }
            var recipients = await (from user in db.Users where user.IsApproved
                join role in db.UserRoles on user.Id equals role.UserId
                join namedRole in db.Roles on role.RoleId equals namedRole.Id
                where namedRole.Name == KcasRoles.ComplianceAdministrator || namedRole.Name == KcasRoles.ComplianceApprover
                select user.Id).Distinct().OrderBy(x => x).ToListAsync();
            if (recipients.Count == 0) throw new ValidationException("No approved Compliance Administrator or Approver is available to receive the Codex task.");
            var task = new ComplianceTask
            {
                ClientId = clientId, TaskType = ComplianceTaskTypes.CodexReview, Status = ComplianceWorkStatuses.Open,
                Owner = ComplianceWorkService.ComplianceReviewAudience, Priority = "High",
                Title = $"Codex client compliance review: {model.Client.DisplayName}",
                Description = Brief(model), LinkedEntityType = nameof(ClientCodexReviewRequest),
                UpdatedBy = actor.Email ?? actor.UserName
            };
            foreach (var initial in await db.ComplianceTasks.Where(x => x.ClientId == clientId &&
                x.TaskType == ComplianceTaskTypes.TriggerReview && x.ClientRiskAssessmentId == null &&
                x.Status == ComplianceWorkStatuses.Open).ToListAsync())
            {
                initial.Status = ComplianceStatuses.Withdrawn;
                initial.ClosureReason = "Initial preparation is tracked by the scoped Codex handoff; no check has been marked complete.";
            }
            db.ClientCodexReviewRequests.Add(new() { ClientId = clientId, Task = task, MaterialHash = model.MaterialHash,
                Brief = task.Description, RecipientUserIdsJson = JsonSerializer.Serialize(recipients, Json) });
            Audit(db, clientId, "CodexReviewRequested", actor, Required(reason, "Reason"), new { model.MaterialHash, Recipients = recipients });
        });

    public async Task ValidateRecordedResultsAsync(int clientId, ClaimsPrincipal principal, string? completionSummary = null)
        => await WriteAsync(clientId, principal, async (db, actor, model) =>
        {
            await RequirePrepareAsync(db, actor.Id);
            var request = model.Request ?? throw new ValidationException("No Codex handoff is recorded.");
            if (request.Status is not ("AwaitingCodex" or "ResultsRecorded")) throw new ValidationException("Request a current review before validating results.");
            if (request.MaterialHash != model.MaterialHash) throw new ValidationException("The client or requested scope changed. Request a fresh Codex review.");
            if (!model.IsReady) throw new ValidationException(string.Join(" ", model.IntakeBlockers.Concat(model.CheckBlockers)));
            request.Status = "ResultsRecorded";
            request.CompletedAtUtc = DateTime.UtcNow;
            request.CompletedContentHash = model.ContentHash;
            request.CompletionSummary = completionSummary is null
                ? request.CompletionSummary ?? "Current evidence and assessment prerequisites validated; KI decision remains separate."
                : Required(completionSummary, "Review findings");
            request.Task.Status = ComplianceStatuses.Closed;
            request.Task.ClosedAtUtc = DateTime.UtcNow;
            request.Task.ClosedBy = actor.Email ?? actor.UserName;
            request.Task.ClosureReason = request.CompletionSummary;
            Audit(db, clientId, "RecordedResultsValidated", actor, request.CompletionSummary, new { model.ContentHash });
        });

    public async Task DecideAsync(int clientId, string expectedHash, string decision, string reason, ClaimsPrincipal principal)
        => await WriteAsync(clientId, principal, async (db, actor, model) =>
        {
            var ki = await KiAsync(db, actor) ?? throw new UnauthorizedAccessException("Only a current authorised KI in the governance register may decide client acceptance.");
            if (expectedHash != model.ContentHash) throw new ValidationException("The compliance record changed. Review the current summary before deciding.");
            if (decision is not ("Accepted" or "Declined" or "ReturnForClarification")) throw new ValidationException("Select a valid KI decision.");
            if (!model.Client.RequiresClientAcceptance) throw new ValidationException("Start a deliberate acceptance review for this existing relationship first.");
            if (decision == "Accepted")
            {
                if (!model.IsReady) throw new ValidationException(string.Join(" ", model.IntakeBlockers.Concat(model.CheckBlockers)));
                if (model.Request is { Status: "AwaitingCodex" }) throw new ValidationException("Validate the recorded check results before KI acceptance.");
                if (model.Request is { Status: "ResultsRecorded" } completed && completed.CompletedContentHash != model.ContentHash)
                    throw new ValidationException("The check results changed after validation. Validate the current results before KI acceptance.");
                if (model.IsAccepted) throw new ValidationException("This current record is already accepted.");
                var assessment = model.Assessment!;
                if (assessment.Status == ClientRiskAssessmentStatuses.PendingKiApproval)
                {
                    assessment.Status = ClientRiskAssessmentStatuses.Approved;
                    assessment.ApprovedAtUtc = DateTime.UtcNow;
                    assessment.UpdatedAtUtc = DateTime.UtcNow;
                    assessment.Approvals.Add(new() { Approver = actor.Email ?? actor.UserName ?? actor.Id, Reason = Required(reason, "Reason") });
                    foreach (var older in await db.ClientRiskAssessments.Where(x => x.ClientId == clientId && x.Id != assessment.Id &&
                        (x.Status == ClientRiskAssessmentStatuses.Finalised || x.Status == ClientRiskAssessmentStatuses.Approved)).ToListAsync())
                        older.Status = ClientRiskAssessmentStatuses.Superseded;
                    model = await BuildAsync(db, clientId, assessment);
                }
            }
            var record = new ClientAcceptanceDecision
            {
                ClientId = clientId, Decision = decision, ContentHash = model.ContentHash,
                SnapshotJson = SummaryJson(model), Reason = Required(reason, "Reason"), DecidedByUserId = actor.Id,
                DecidedBy = actor.Email ?? actor.UserName ?? actor.Id, GovernanceRoleAssignmentId = ki.Id
            };
            db.ClientAcceptanceDecisions.Add(record);
            Audit(db, clientId, "ClientAcceptanceDecision", actor, record.Reason, new { decision, model.ContentHash, KiAssignment = ki.Id });
        });

    public async Task StartFreshReviewAsync(int clientId, string reason, ClaimsPrincipal principal)
        => await WriteAsync(clientId, principal, async (db, actor, model) =>
        {
            await RequirePrepareAsync(db, actor.Id);
            model.Client.RequiresClientAcceptance = true;
            if (model.Profile is not null) model.Profile.Version = Guid.NewGuid().ToString("N");
            if (model.Request is { } request) { request.Status = "Superseded"; request.Task.Status = ComplianceStatuses.Withdrawn; }
            db.ClientAcceptanceDecisions.Add(new() { ClientId = clientId, Decision = "ReviewStarted", Reason = Required(reason, "Reason"),
                ContentHash = model.ContentHash, SnapshotJson = SummaryJson(model), DecidedByUserId = actor.Id, DecidedBy = actor.Email ?? actor.Id });
            Audit(db, clientId, "AcceptanceReviewStarted", actor, reason, new { model.ContentHash });
        });

    public async Task<IReadOnlyList<ClientCodexNotification>> NotificationsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await ActorAsync(db, principal);
        if (!await (from link in db.UserRoles where link.UserId == actor.Id join role in db.Roles on link.RoleId equals role.Id
            where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover select link).AnyAsync())
            return [];
        var admin = await IsAdminAsync(db, actor.Id);
        var rows = await db.ClientCodexReviewRequests.AsNoTracking().Include(x => x.Client)
            .Where(x => x.Status == "AwaitingCodex" && (admin || !x.Client.ExcludeFromComplianceLists)).OrderBy(x => x.CreatedAtUtc).ToListAsync();
        return rows.Where(x => (JsonSerializer.Deserialize<string[]>(x.RecipientUserIdsJson, Json) ?? []).Contains(actor.Id))
            .Select(x => new ClientCodexNotification(x.ClientId, x.Client.DisplayName, "Awaiting Codex review", x.Brief)).ToList();
    }

    public static async Task RequireAcceptedAsync(ApplicationDbContext db, int clientId)
    {
        var client = await db.Clients.AsNoTracking().SingleAsync(x => x.Id == clientId);
        var sanctionsBlockers = await ClientSanctionsCoverageService.BlockersAsync(db, clientId);
        if (sanctionsBlockers.Count > 0) throw new ValidationException(string.Join(" ", sanctionsBlockers));
        if (!client.RequiresClientAcceptance) return; // Explicit transition: no invented acceptance for existing relationships.
        var model = await BuildAsync(db, clientId, tracked: false);
        if (!model.IsAccepted) throw new ValidationException("Complete the current client checks and obtain KI acceptance before approved issue or implementation.");
    }

    private async Task WriteAsync(int clientId, ClaimsPrincipal principal, Func<ApplicationDbContext, ApplicationUser, ClientOnboardingModel, Task> operation)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await ActorAsync(db, principal);
        await VisibleAsync(db, await db.Clients.AsNoTracking().SingleAsync(x => x.Id == clientId), actor.Id);
        var model = await BuildAsync(db, clientId);
        await VisibleAsync(db, model.Client, actor.Id);
        await operation(db, actor, model);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    internal static async Task<ClientOnboardingModel> BuildAsync(ApplicationDbContext db, int clientId, ClientRiskAssessment? assessmentOverride = null, bool tracked = true)
    {
        var clients = db.Clients.Include(x => x.PersonalProfile).Include(x => x.Addresses).Include(x => x.ContactPoints)
            .Include(x => x.RelatedParties).ThenInclude(x => x.Roles).Include(x => x.EntityProfile)
            .AsSplitQuery();
        var client = await (tracked ? clients : clients.AsNoTrackingWithIdentityResolution()).SingleOrDefaultAsync(x => x.Id == clientId)
            ?? throw new KeyNotFoundException("Client not found.");
        var profiles = db.ClientOnboardingProfiles.AsQueryable();
        var profile = await (tracked ? profiles : profiles.AsNoTracking()).SingleOrDefaultAsync(x => x.ClientId == clientId);
        var evidence = await new ClientEvidenceReadinessService(db).LoadClientReadinessAsync(clientId);
        var assessments = db.ClientRiskAssessments.Include(x => x.Responses).ThenInclude(x => x.SelectedOption)
            .Include(x => x.MethodologyVersion).ThenInclude(x => x!.Factors)
            .Include(x => x.Approvals).Where(x => x.ClientId == clientId && x.Status != ClientRiskAssessmentStatuses.Superseded)
            .OrderByDescending(x => x.Id);
        var assessment = assessmentOverride ?? await (tracked ? assessments : assessments.AsNoTracking()).FirstOrDefaultAsync();
        var model = new ClientOnboardingModel { Client = client, Profile = profile, Evidence = evidence, Assessment = assessment,
            Decisions = await db.ClientAcceptanceDecisions.AsNoTracking().Where(x => x.ClientId == clientId).OrderByDescending(x => x.Id).ToListAsync(),
            Request = await (tracked ? db.ClientCodexReviewRequests.Include(x => x.Task) : db.ClientCodexReviewRequests.Include(x => x.Task).AsNoTracking())
                .Where(x => x.ClientId == clientId).OrderByDescending(x => x.Id).FirstOrDefaultAsync() };
        model.MaterialHash = Hash(new { client.ClientCategory, client.FullName, client.SurnameOrEntityName, client.PersonalProfile?.SouthAfricanIdNumber,
            client.PersonalProfile?.TaxNumber,
            Entity = client.EntityProfile is null ? null : new { client.EntityProfile.LegalForm, client.EntityProfile.RegistrationNumber,
                client.EntityProfile.RegistrationCountry, client.EntityProfile.NatureOfBusinessOrPurpose },
            Addresses = client.Addresses.OrderBy(x => x.Id).Select(x => new { x.AddressType, x.LinesRaw }),
            Parties = client.RelatedParties.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => new { x.Id, x.DisplayName, x.SouthAfricanIdNumber, x.PassportNumber, x.PassportCountry, x.BirthDate, x.CountryOfResidence, x.OwnershipPercent, x.AuthorityBasis, x.ControlBasis, Roles = x.Roles.Select(r => r.RoleCode).OrderBy(x => x) }),
            profile?.RequestedService, profile?.PurposeAndProposedFunds });
        model.CheckBlockers.AddRange(evidence.Requirements.Where(x => x.IsBlocked).Select(x => $"{x.Title}: required check/evidence is unresolved."));
        model.CheckBlockers.AddRange(evidence.OwnershipBlockers);
        model.CheckBlockers.AddRange(await ClientSanctionsCoverageService.BlockersAsync(db, clientId));
        if (await db.ClientVerificationItems.AnyAsync(x => x.ClientId == clientId && x.IsBlocking && x.Status == ClientVerificationStatuses.Pending))
            model.CheckBlockers.Add("Resolve the blocking client-fact conflicts.");
        if (assessment is null || assessment.Status is not (ClientRiskAssessmentStatuses.Finalised or ClientRiskAssessmentStatuses.Approved or ClientRiskAssessmentStatuses.PendingKiApproval))
            model.CheckBlockers.Add("Complete and finalise the evidence-based compliance risk assessment.");
        else
        {
            if (assessment.MethodologyVersion?.Status is ComplianceStatuses.Draft or ComplianceStatuses.Rejected or ComplianceStatuses.Superseded)
                model.CheckBlockers.Add("The assessment methodology is no longer usable.");
            if (assessment.MethodologyVersion is null || assessment.MethodologyVersion.Factors.Any(f => !assessment.Responses.Any(r => r.RiskFactorDefinitionId == f.Id)))
                model.CheckBlockers.Add("The assessment does not cover every methodology factor.");
            if (assessment.HasSanctionsConcern) model.CheckBlockers.Add("Resolve the sanctions/TFS concern; no KI override is permitted.");
            if (assessment.Responses.Count == 0 || assessment.Responses.Any(x => x.RiskFactorOptionId is null || string.IsNullOrWhiteSpace(x.Explanation) || x.ConfirmedAtUtc is null))
                model.CheckBlockers.Add("Every applicable risk factor needs a supported confirmed answer.");
            if (assessment.NextReviewDate < DateOnly.FromDateTime(DateTime.Today)) model.CheckBlockers.Add("The compliance assessment is overdue for review.");
            if (model.Request is { } request && request.MaterialHash != model.MaterialHash)
                model.CheckBlockers.Add("The client/party or service scope changed. Request a fresh Codex review.");
            if (model.Request is { Status: "AwaitingCodex" } pending && pending.MaterialHash == model.MaterialHash &&
                pending.ImportSourceReference is null && pending.CreatedAtUtc > assessment.UpdatedAtUtc && model.Decisions.Any(x => x.Decision == "Accepted" && x.ImportSourceReference == null))
                model.CheckBlockers.Add("Refresh the risk assessment for the new review scope.");
        }
        foreach (var item in evidence.EvidenceItems.Where(x => x.IsCurrentSelection && x.EscalationRequired))
            model.CheckBlockers.Add($"{item.Title}: unresolved screening escalation.");
        var storedEvidence = await db.ClientEvidenceItems.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Id).ToListAsync();
        var changedScope = model.Request is { } currentRequest && await db.ClientCodexReviewRequests.AsNoTracking()
            .AnyAsync(x => x.ClientId == clientId && x.Id < currentRequest.Id && x.ImportSourceReference == null && x.MaterialHash != currentRequest.MaterialHash);
        if (changedScope && assessment?.UpdatedAtUtc < model.Request!.CreatedAtUtc)
            model.CheckBlockers.Add("Reconfirm the assessment against the changed client/party and service scope.");
        foreach (var subject in evidence.ScreeningSubjects.Where(x => x.ClientRelatedPartyId.HasValue || x.SubjectType != ClientEvidenceScreeningSubjectTypes.Other))
        foreach (var type in new[] { "PepPip", "SanctionsTfs", "AdverseInformation" })
        {
            var current = evidence.EvidenceItems.Where(x => x.EvidenceType == type && x.IsCurrentSelection &&
                string.Equals(x.ScreeningSubjectName, subject.SubjectName, StringComparison.OrdinalIgnoreCase) &&
                x.ScreeningSubjectType == subject.SubjectType &&
                storedEvidence.Any(raw => raw.Id == x.Id && raw.ClientRelatedPartyId == subject.ClientRelatedPartyId))
                .OrderByDescending(x => x.ScreeningReviewedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
            if (current is null || current.Status != ClientEvidenceStatuses.Verified ||
                !ClientEvidenceOwnershipStatuses.IsActive(current.OwnershipStatus) || current.ScreeningReviewedAtUtc is null ||
                current.ScreeningReviewedAtUtc > DateTime.UtcNow || string.IsNullOrWhiteSpace(current.ScreeningPerformedBy) ||
                string.IsNullOrWhiteSpace(current.ScreeningSources) || string.IsNullOrWhiteSpace(current.Notes) ||
                !ClientEvidenceScreeningOutcomes.ForEvidenceType(type).Contains(current.ScreeningOutcome ?? "") ||
                current.ExpiryDate < DateOnly.FromDateTime(DateTime.Today))
                model.CheckBlockers.Add($"{subject.Label}: {type} needs a current supported screening result, performer, time and sources.");
            else if (current.ScreeningOutcome == ClientEvidenceScreeningOutcomes.PossibleMatch ||
                type == "SanctionsTfs" && current.ScreeningOutcome != ClientEvidenceScreeningOutcomes.NoMatch)
                model.CheckBlockers.Add($"{subject.Label}: resolve the {type} match before acceptance.");
            else if (changedScope && current.ScreeningReviewedAtUtc < model.Request!.CreatedAtUtc)
                model.CheckBlockers.Add($"{subject.Label}: reconfirm {type} for the changed review scope; retain the earlier result.");
            else if (current.ScreeningRiskSignal == ClientEvidenceRiskSignals.High && assessment?.RequiresEdd != true)
                model.CheckBlockers.Add($"{subject.Label}: the assessment must address the high-risk {type} finding and enhanced measures.");
        }
        if (client.RequiresClientAcceptance)
        {
            var root = await db.ClientEvidenceScanRoots.AsNoTracking().Where(x => x.IsActive).OrderByDescending(x => x.Id).Select(x => x.RootPath).FirstOrDefaultAsync();
            foreach (var item in evidence.EvidenceItems.Where(x => x.IsCurrentSelection && x.Status == ClientEvidenceStatuses.Verified && x.CanOpen))
            {
                var raw = storedEvidence.Single(x => x.Id == item.Id);
                var path = ClientEvidenceFileResolver.ResolveExistingPath(raw.SourcePath, raw.RelativePath, raw.FileName, client.ClientFolder, root);
                if (path is null) { model.CheckBlockers.Add($"{item.Title}: linked evidence file is unavailable."); continue; }
                if (string.IsNullOrWhiteSpace(raw.FileSha256)) { model.CheckBlockers.Add($"{item.Title}: record the verified file fingerprint before acceptance."); continue; }
                try
                {
                    await using var stream = File.OpenRead(path);
                    var currentHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                    if (!string.Equals(currentHash, raw.FileSha256, StringComparison.OrdinalIgnoreCase))
                        model.CheckBlockers.Add($"{item.Title}: evidence file changed since verification.");
                }
                catch (IOException) { model.CheckBlockers.Add($"{item.Title}: evidence file cannot currently be verified."); }
                catch (UnauthorizedAccessException) { model.CheckBlockers.Add($"{item.Title}: evidence file is not readable by KCAS."); }
            }
        }
        if (profile is null || string.IsNullOrWhiteSpace(profile.RequestedService) || string.IsNullOrWhiteSpace(profile.ResponsibleRepresentative) || string.IsNullOrWhiteSpace(profile.PurposeAndProposedFunds))
            model.IntakeBlockers.Add("Record requested service, responsible representative, purpose and proposed funds.");
        if (profile?.DisclosureDeliveredAtUtc is null || string.IsNullOrWhiteSpace(profile.DisclosureVersion) || string.IsNullOrWhiteSpace(profile.DisclosureDeliveryReference))
            model.IntakeBlockers.Add("Record the actual initial disclosures, version and delivery reference.");
        if (assessment is { RequiresEdd: true } || assessment is { IsOverride: true })
            if (string.IsNullOrWhiteSpace(profile?.EnhancedMeasures)) model.IntakeBlockers.Add("Record enhanced measures and monitoring arrangements before KI acceptance.");
        model.ContentHash = Hash(new { model.MaterialHash, profile?.Version, profile?.ResponsibleRepresentative, profile?.DisclosureVersion,
            profile?.DisclosureDeliveredAtUtc, profile?.DisclosureDeliveryReference, profile?.EnhancedMeasures,
            Assessment = assessment is null ? null : new { assessment.Id, assessment.Status, assessment.UpdatedAtUtc, assessment.SnapshotJson },
            Responses = assessment is null ? [] : await db.ClientRiskAssessmentResponses.AsNoTracking()
                .Where(x => x.ClientRiskAssessmentId == assessment.Id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.RiskFactorDefinitionId, x.RiskFactorOptionId, x.ClientEvidenceItemId, x.Explanation, x.ConfirmedBy, x.ConfirmedAtUtc }).ToListAsync(),
            Evidence = evidence.EvidenceItems.OrderBy(x => x.Id).Select(x => new { x.Id, x.EvidenceType, x.RelativePath, x.FileName, x.Reviewer, x.RecordedAtUtc, x.VerifiedDate, x.ExpiryDate, x.Status, x.OwnershipStatus, x.SelectionStatus,
                x.ScreeningPerformedBy, x.ScreeningReviewedAtUtc, x.ScreeningSources, x.ScreeningSubjectName, x.ScreeningOutcome, x.ScreeningRiskSignal, x.EscalationRequired, x.Notes }),
            Exceptions = await db.ClientEvidenceExceptions.AsNoTracking().Where(x => x.ClientId == clientId && x.IsActive).OrderBy(x => x.Id).Select(x => new { x.Id, x.Reason, x.ReviewDate, x.ApprovedAtUtc }).ToListAsync(),
            Files = storedEvidence.Select(x => new { x.Id, x.SourcePath, x.FileSha256, x.FileSizeBytes, x.ClientRelatedPartyId }),
            model.CheckBlockers, model.IntakeBlockers });
        model.ChecksContentHash = model.ContentHash;
        model.BraRiskReport = await db.ClientBraRiskReports.AsNoTracking().Where(x => x.ClientId == clientId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (model.BraRiskReport is { } braReport)
            model.ContentHash = Hash(new { model.ChecksContentHash, braReport.Id, braReport.ContentJson, braReport.BraReference,
                braReport.MethodVersion, braReport.SourceContentHash, braReport.ImportPackageId, braReport.PerformedBy, braReport.RecordedAtUtc });
        model.ImportedReview = await ClientOnboardingTransfer.LoadReceiptAsync(db, clientId);
        return model;
    }

    internal static string Brief(ClientOnboardingModel model) => $"Client: {model.Client.DisplayName}; KCAS client ID: {model.Client.Id}; Kanaan ID: {model.Client.KanaanId}.\n" +
        $"Evidence folder: {model.Client.ClientFolder ?? "Not recorded"}.\nRequested service: {model.Profile?.RequestedService ?? "Identify from the client folder and existing KCAS records"}.\n" +
        $"Scope reference: {model.MaterialHash}.\n" +
        "Prepare the review from the first outstanding step through to a KI-ready summary. Read the actual client folder, correspondence, mandates, advice and existing KCAS records. Identify and record the requested service, responsible representative, purpose/funds, actual disclosure version/delivery evidence and applicable enhanced measures where supported. Missing preparation is part of this Codex task; do not require manual entry before beginning. Retain genuine gaps for confirmation; do not invent dates or claim delivery from an unsigned template.\n" +
        "Read the actual evidence; complete current client and applicable-party screening, CDD and supported risk-factor answers. Reuse valid records; do not scan folders or invent results.\n" +
        "PEP/PIP and adverse-information research: perform live Google or equivalent web searches for each client and applicable natural person, including beneficial owners and persons acting on the client's behalf. For joint/entity records, identify and search the underlying natural persons as well as the entity; searching only a combined account name is insufficient. Use full names, supported name variants and relevant public context such as occupation, employer and country to distinguish namesakes. Do not put private identity numbers, account numbers or contact details into public search queries.\n" +
        "Check public-office and relevant prominent-business roles against the applicable FIC Act Schedules 3A, 3B and 3C, including supported family/close-associate exposure. Research credible adverse information separately; PEP/PIP status is not itself an adverse finding or wrongdoing. Open and assess the underlying sources, prioritising official records and reliable reporting; do not rely on search snippets or an AI summary alone. A maintained PEP list or paid database is not a prerequisite for this handoff.\n" +
        "For each subject, retain the actual search queries, engine, checked source URLs/titles, source dates where available, actual check date/time, identity comparison, findings and coverage limitations. Record separate PepPip and AdverseInformation results with Codex attribution: no relevant finding in the sources searched is not proof that no exposure exists. Unresolved namesakes, inaccessible sources or unavailable live search remain explicitly outstanding, not a fabricated clear result. Preserve valid earlier reviews as history and do not re-date them as fresh searches. Present material findings and applicable enhanced measures to the KI; do not make the KI decision.\n" +
        "Sanctions/TFS is a separate check against the current applicable official FIC/UN list and recorded version, not a general web-search substitute.\n" +
        "Preserve an existing client's lifecycle, supported reviews and history; distinguish historical relationship evidence from a new client instruction.\n" +
        "Prepare a separate BRA-linked ML/TF/PF proposal where supported: same scenario before and after controls, likelihood and impact 1-3 with reasons, actual mitigating evidence and limitations. Use ClientBraRiskReportService; do not automatically lower risk or replace the formal client rating. A missing proposal is not a fabricated clearance.\n" +
        string.Join("\n", model.Evidence.ScreeningSubjects.Select(x => $"Screen: {x.Label}.")) + "\n" +
        "Preparation gaps:\n" + string.Join("\n", model.IntakeBlockers) + "\nCheck gaps:\n" + string.Join("\n", model.CheckBlockers) + "\n" +
        "Save actual findings, evidence links, Codex performer, sources/list versions, actual date/time and limitations through authorised KCAS services. Refresh the scoped request if preparation reveals a material service/party change. Validate recorded results in the onboarding page and prepare the substantive KI summary. Report only remaining unsupported facts or decisions. The authorised KI records acceptance; do not impersonate that decision.";

    private static string SummaryJson(ClientOnboardingModel model) => JsonSerializer.Serialize(new { model.Client.Id, model.Client.DisplayName,
        Preparation = model.Profile is null ? null : new { model.Profile.Version, model.Profile.RequestedService,
            model.Profile.ResponsibleRepresentative, model.Profile.PurposeAndProposedFunds, model.Profile.DisclosureVersion,
            model.Profile.DisclosureDeliveredAtUtc, model.Profile.DisclosureDeliveryReference, model.Profile.EnhancedMeasures },
        model.MaterialHash, model.ContentHash,
        Assessment = model.Assessment is null ? null : new { model.Assessment.Id, model.Assessment.FinalRating, model.Assessment.RequiresEdd, model.Assessment.Narrative, Responses = model.Assessment.Responses.Select(x => new { x.RiskFactorDefinitionId, x.Explanation, x.ClientEvidenceItemId, x.ConfirmedBy, x.ConfirmedAtUtc }) },
        model.BraRiskReport, model.Evidence.Requirements, model.Evidence.ScreeningSubjects, model.Evidence.EvidenceItems, model.CheckBlockers, model.IntakeBlockers }, Json);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, FingerprintJson))));
    // MySQL datetime(6) has no timezone and stores microseconds; both sides must hash identically.
    private sealed class DatabaseTimestampConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc).ToString("O"));
    }
    private static string Required(string? value, string label) => !string.IsNullOrWhiteSpace(value) && value.Length <= 20000 ? value.Trim() : throw new ValidationException($"{label} is required (maximum 20000 characters).");
    private static async Task<ApplicationUser> ActorAsync(ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
        if (principal.Identity?.IsAuthenticated != true || user?.IsApproved != true ||
            !(await PermissionAsync(db, user.Id, KcasPermissions.ClientsView) || await PermissionAsync(db, user.Id, KcasPermissions.RiskAssessmentsView)))
            throw new UnauthorizedAccessException("An approved current client/compliance account is required.");
        return user;
    }
    private static Task<bool> PermissionAsync(ApplicationDbContext db, string id, string permission) =>
        (from role in db.UserRoles where role.UserId == id join claim in db.RoleClaims on role.RoleId equals claim.RoleId
            where claim.ClaimType == KcasClaimTypes.Permission && claim.ClaimValue == permission select role).AnyAsync();
    private static async Task<bool> CanPrepareAsync(ApplicationDbContext db, string id) => await PermissionAsync(db, id, KcasPermissions.KycManage) || await PermissionAsync(db, id, KcasPermissions.ComplianceManage);
    private static async Task RequirePrepareAsync(ApplicationDbContext db, string id)
    { if (!await CanPrepareAsync(db, id)) throw new UnauthorizedAccessException("Current compliance-preparation permission is required."); }
    private static Task<bool> IsAdminAsync(ApplicationDbContext db, string id) => (from link in db.UserRoles where link.UserId == id join role in db.Roles on link.RoleId equals role.Id where role.Name == KcasRoles.Administrator select role).AnyAsync();
    private static async Task VisibleAsync(ApplicationDbContext db, Client client, string id)
    { if (client.ExcludeFromComplianceLists && !await IsAdminAsync(db, id)) throw new UnauthorizedAccessException("This client is restricted to administrators."); }
    private static async Task<GovernanceRoleAssignment?> KiAsync(ApplicationDbContext db, ApplicationUser actor)
    {
        if (!await PermissionAsync(db, actor.Id, KcasPermissions.ClientsManage)) return null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (await db.GovernanceRoleAssignments.Where(x => x.IsActive && (x.StartDate == null || x.StartDate <= today) && (x.EndDate == null || x.EndDate >= today)).ToListAsync())
            .FirstOrDefault(x => ComplianceApprovalRules.IsKeyIndividualRole(x.RoleType) && !string.IsNullOrWhiteSpace(x.Email) && string.Equals(x.Email, actor.Email, StringComparison.OrdinalIgnoreCase));
    }
    private static void Audit(ApplicationDbContext db, int clientId, string action, ApplicationUser actor, string reason, object value)
        => db.ComplianceAuditEvents.Add(new() { EntityType = nameof(ClientOnboardingProfile), EntityId = clientId, Action = action,
            UserName = actor.Email ?? actor.UserName ?? actor.Id, Reason = reason, NewValueJson = JsonSerializer.Serialize(value, Json), TimestampUtc = DateTime.UtcNow });
}
