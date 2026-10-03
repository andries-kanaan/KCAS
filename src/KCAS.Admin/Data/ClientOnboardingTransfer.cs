using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientOnboardingPreparationPackage
{
    public string Version { get; set; } = "";
    public string RequestedService { get; set; } = "";
    public string ResponsibleRepresentative { get; set; } = "";
    public string PurposeAndProposedFunds { get; set; } = "";
    public string DisclosureVersion { get; set; } = "";
    public DateTime? DisclosureDeliveredAtUtc { get; set; }
    public string DisclosureDeliveryReference { get; set; } = "";
    public string EnhancedMeasures { get; set; } = "";
    public DateTime? RelationshipCommencedAtUtc { get; set; }
    public string RelationshipAuthorityReference { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class ClientOnboardingHandoffPackage
{
    public string SourceKey { get; set; } = "";
    public string Status { get; set; } = "";
    public string MaterialHash { get; set; } = "";
    public string Brief { get; set; } = "";
    public List<string> Recipients { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedContentHash { get; set; }
    public string? CompletionSummary { get; set; }
}

public sealed class ClientAcceptanceDecisionPackage
{
    public string SourceKey { get; set; } = "";
    public string OriginEnvironment { get; set; } = "";
    public string Decision { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string SnapshotSha256 { get; set; } = "";
    public string Reason { get; set; } = "";
    public string DecidedByUserId { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public int GovernanceRoleAssignmentId { get; set; }
    public string? SourceKiName { get; set; }
    public string? SourceKiRole { get; set; }
    public DateTime DecidedAtUtc { get; set; }
}

public sealed class ClientOnboardingTransferPackage
{
    public string SourceKey { get; set; } = "";
    public string RevisionHash { get; set; } = "";
    public string SourceMaterialHash { get; set; } = "";
    public string SourceContentHash { get; set; } = "";
    public bool CurrentResultsValidated { get; set; }
    public string? LatestReviewKey { get; set; }
    public ClientOnboardingPreparationPackage? Preparation { get; set; }
    public List<ClientOnboardingHandoffPackage> Reviews { get; set; } = [];
    public List<ClientAcceptanceDecisionPackage> Decisions { get; set; } = [];
    public Dictionary<int, string> EvidenceReferences { get; set; } = [];
}

public sealed class ClientOnboardingImportReceipt
{
    public string PackageId { get; set; } = "";
    public string SourceEnvironment { get; set; } = "";
    public DateTime SourceExportedAtUtc { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public string ImportedBy { get; set; } = "";
    public string AppliedFingerprint { get; set; } = "";
    public ClientOnboardingTransferPackage Source { get; set; } = new();
    public Dictionary<string, int> EvidenceIds { get; set; } = [];
}

internal static class ClientOnboardingTransfer
{
    private const string DecisionPrefix = "onboarding:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new TimestampConverter() } };

    public static async Task<ClientOnboardingTransferPackage?> ExportAsync(ApplicationDbContext db,
        Client client, ClientReviewPackage package, CancellationToken cancellationToken)
    {
        var model = await ClientOnboardingService.BuildAsync(db, client.Id, tracked: false);
        var reviews = await db.ClientCodexReviewRequests.AsNoTracking().Where(x => x.ClientId == client.Id && x.ImportSourceReference == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        if (model.Profile is null && reviews.Count == 0 && model.Decisions.Count == 0 && model.ImportedReview is null) return null;
        var source = new ClientOnboardingTransferPackage {
            SourceKey = Hash(new { package.SourceEnvironment, package.Client.LegacyClientId, package.Client.KanaanId }),
            SourceMaterialHash = model.MaterialHash, SourceContentHash = model.ContentHash,
            CurrentResultsValidated = model.IsReady && (model.IsAccepted || model.Request is { Status: "ResultsRecorded" } request &&
                request.CompletedContentHash == model.ContentHash && request.MaterialHash == model.MaterialHash),
            Preparation = model.Profile is null ? null : Preparation(model.Profile),
            Reviews = model.ImportedReview?.Source.Reviews.ToList() ?? []
        };
        var users = reviews.Count == 0 ? [] : await db.Users.AsNoTracking()
            .Select(x => new { x.Id, Name = x.Email ?? x.UserName ?? x.Id }).ToListAsync(cancellationToken);
        foreach (var review in reviews)
        {
            var recipientIds = (JsonSerializer.Deserialize<string[]>(review.RecipientUserIdsJson) ?? []).ToHashSet(StringComparer.Ordinal);
            var names = users.Where(x => recipientIds.Contains(x.Id)).Select(x => x.Name);
            source.Reviews.Add(new() { SourceKey = Hash(new { source.SourceKey, RequestId = review.Id }), Status = review.Status,
                MaterialHash = review.MaterialHash, Brief = review.Brief, Recipients = names.Order().ToList(), CreatedAtUtc = review.CreatedAtUtc,
                CompletedAtUtc = review.CompletedAtUtc, CompletedContentHash = review.CompletedContentHash, CompletionSummary = review.CompletionSummary });
        }
        source.LatestReviewKey = model.Request is { ImportSourceReference: null } latest
            ? Hash(new { source.SourceKey, RequestId = latest.Id }) : model.ImportedReview?.Source.LatestReviewKey;
        foreach (var decision in model.Decisions.OrderBy(x => x.Id))
        {
            if (decision.ImportSourceReference is { } importedKey)
            {
                var original = model.ImportedReview?.Source.Decisions.SingleOrDefault(x => DecisionPrefix + x.SourceKey == importedKey);
                if (original is null) throw new ValidationException("The original imported KI decision provenance is unavailable for export.");
                source.Decisions.Add(original);
                continue;
            }
            var ki = await db.GovernanceRoleAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == decision.GovernanceRoleAssignmentId, cancellationToken);
            source.Decisions.Add(new() { SourceKey = Hash(new { source.SourceKey, DecisionId = decision.Id }), OriginEnvironment = package.SourceEnvironment,
                Decision = decision.Decision, ContentHash = decision.ContentHash, SnapshotJson = decision.SnapshotJson, SnapshotSha256 = TextHash(decision.SnapshotJson),
                Reason = decision.Reason, DecidedBy = decision.DecidedBy, DecidedByUserId = decision.DecidedByUserId,
                GovernanceRoleAssignmentId = decision.GovernanceRoleAssignmentId, SourceKiName = ki?.PersonName, SourceKiRole = ki?.RoleType, DecidedAtUtc = decision.DecidedAtUtc });
        }
        var packagedKeys = package.Evidence.Select(x => x.EvidenceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in client.EvidenceItems)
        {
            var key = ClientReviewTransferService.EvidenceKey(evidence);
            if (packagedKeys.Contains(key)) source.EvidenceReferences[evidence.Id] = key;
        }
        source.RevisionHash = RevisionHash(source);
        Validate(source, package);
        return source;
    }

    public static void Validate(ClientOnboardingTransferPackage source, ClientReviewPackage package)
    {
        if (!IsHash(source.SourceKey) || !IsHash(source.SourceMaterialHash) || !IsHash(source.SourceContentHash) ||
            source.Reviews is null || source.Decisions is null || source.EvidenceReferences is null || source.Reviews.Any(x => x is null) || source.Decisions.Any(x => x is null) ||
            source.Reviews.Count > 2000 || source.Decisions.Count > 2000 || source.RevisionHash != RevisionHash(source))
            throw new ValidationException("Acceptance transfer provenance or revision fingerprint is invalid.");
        if (source.Preparation is { } prep && (string.IsNullOrWhiteSpace(prep.Version) || prep.Version.Length > 64 ||
            string.IsNullOrWhiteSpace(prep.UpdatedBy) || prep.UpdatedBy.Length > 191 || !ValidTime(prep.UpdatedAtUtc) ||
            new[] { prep.RequestedService, prep.ResponsibleRepresentative, prep.PurposeAndProposedFunds, prep.DisclosureVersion,
                prep.DisclosureDeliveryReference, prep.EnhancedMeasures, prep.RelationshipAuthorityReference }.Any(x => x is null || x.Length > 20000) ||
            prep.DisclosureDeliveredAtUtc > DateTime.UtcNow.AddMinutes(1) ||
            prep.RelationshipCommencedAtUtc > DateTime.UtcNow.AddMinutes(1)))
            throw new ValidationException("Acceptance preparation version, author or actual dates are invalid.");
        if (source.Reviews.Select(x => x.SourceKey).Distinct().Count() != source.Reviews.Count || source.Decisions.Select(x => x.SourceKey).Distinct().Count() != source.Decisions.Count)
            throw new ValidationException("Acceptance history source keys are duplicated.");
        foreach (var review in source.Reviews)
            if (!IsHash(review.SourceKey) || !IsHash(review.MaterialHash) || review.Status is not ("AwaitingCodex" or "ResultsRecorded" or "Superseded") ||
                !ValidTime(review.CreatedAtUtc) || review.Recipients is null || string.IsNullOrWhiteSpace(review.Brief) ||
                review.CompletedAtUtc is { } time && (!ValidTime(time) || time < review.CreatedAtUtc) ||
                review.Status == "ResultsRecorded" && (review.CompletedAtUtc is null || !IsHash(review.CompletedContentHash) || string.IsNullOrWhiteSpace(review.CompletionSummary)))
                throw new ValidationException("A transferred Codex handoff has incomplete source findings or provenance.");
        if (source.LatestReviewKey is not null && !source.Reviews.Any(x => x.SourceKey == source.LatestReviewKey))
            throw new ValidationException("The current source Codex handoff is missing from its history.");
        foreach (var decision in source.Decisions)
        {
            if (!IsHash(decision.SourceKey) || !IsHash(decision.ContentHash) || string.IsNullOrWhiteSpace(decision.OriginEnvironment) ||
                string.IsNullOrWhiteSpace(decision.Reason) || string.IsNullOrWhiteSpace(decision.DecidedBy) || string.IsNullOrWhiteSpace(decision.DecidedByUserId) ||
                decision.DecidedByUserId.Length > 64 || decision.DecidedBy.Length > 191 || !ValidTime(decision.DecidedAtUtc) ||
                decision.Decision is not ("Accepted" or "Declined" or "ReturnForClarification" or "ReviewStarted") ||
                decision.Decision != "ReviewStarted" && decision.GovernanceRoleAssignmentId <= 0 || decision.SnapshotSha256 != TextHash(decision.SnapshotJson))
                throw new ValidationException("A transferred KI decision has incomplete or inconsistent source provenance.");
            try
            {
                using var snapshot = JsonDocument.Parse(decision.SnapshotJson);
                if (snapshot.RootElement.ValueKind != JsonValueKind.Object || !snapshot.RootElement.TryGetProperty("contentHash", out var hash) ||
                    hash.ValueKind != JsonValueKind.String || hash.GetString() != decision.ContentHash)
                    throw new ValidationException("The transferred KI decision does not match its frozen source snapshot.");
            }
            catch (JsonException) { throw new ValidationException("The transferred KI decision snapshot is invalid."); }
        }
        var keys = package.Evidence.Select(x => x.EvidenceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (source.EvidenceReferences.Any(x => x.Key <= 0 || !keys.Contains(x.Value)))
            throw new ValidationException("Acceptance evidence references are missing from the review package.");
    }

    public static async Task<string> PreviewAsync(ApplicationDbContext db, int clientId, ClientReviewPackage package,
        ICollection<string> conflicts, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        var fingerprint = await LocalFingerprintAsync(db, clientId, cancellationToken);
        if (package.Onboarding is not { } source) return fingerprint;
        var receipt = await LoadReceiptAsync(db, clientId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.Source.SourceKey != source.SourceKey) conflicts.Add("The acceptance package comes from a different source lineage; existing live preparation and decisions will be retained.");
            if (receipt.AppliedFingerprint != fingerprint) conflicts.Add("Live acceptance preparation, findings or decisions changed after the previous import. Review those changes before replacing them.");
            if (package.CreatedAtUtc < receipt.SourceExportedAtUtc) conflicts.Add("This acceptance package predates the previously applied source revision.");
        }
        else if (await db.ClientOnboardingProfiles.AnyAsync(x => x.ClientId == clientId, cancellationToken) ||
                 await db.ClientCodexReviewRequests.AnyAsync(x => x.ClientId == clientId, cancellationToken) ||
                 await db.ClientAcceptanceDecisions.AnyAsync(x => x.ClientId == clientId, cancellationToken))
            conflicts.Add("Live already has acceptance preparation or decision history. This package cannot replace locally recorded acceptance work.");
        warnings.Add($"Acceptance transfer: {(source.Preparation is null ? "no preparation" : "service/disclosure preparation")}, {source.Reviews.Count} source handoff(s), {source.Decisions.Count} source decision(s). Live KI confirmation remains available after current prerequisites are satisfied.");
        return fingerprint;
    }

    public static async Task<ClientOnboardingImportReceipt?> ApplyAsync(ApplicationDbContext db, Client client, ClientReviewPackage package,
        IReadOnlyDictionary<string, ClientEvidenceItem> evidence, string actor, string reason, CancellationToken cancellationToken)
    {
        if (package.Onboarding is not { } source) return null;
        if (source.Preparation is { } prep)
        {
            var profile = await db.ClientOnboardingProfiles.SingleOrDefaultAsync(x => x.ClientId == client.Id, cancellationToken);
            if (profile is null) { profile = new() { ClientId = client.Id }; db.ClientOnboardingProfiles.Add(profile); }
            profile.Version = prep.Version; profile.RequestedService = prep.RequestedService; profile.ResponsibleRepresentative = prep.ResponsibleRepresentative;
            profile.PurposeAndProposedFunds = prep.PurposeAndProposedFunds; profile.DisclosureVersion = prep.DisclosureVersion;
            profile.DisclosureDeliveredAtUtc = prep.DisclosureDeliveredAtUtc; profile.DisclosureDeliveryReference = prep.DisclosureDeliveryReference;
            profile.EnhancedMeasures = prep.EnhancedMeasures; profile.RelationshipCommencedAtUtc = prep.RelationshipCommencedAtUtc;
            profile.RelationshipAuthorityReference = prep.RelationshipAuthorityReference; profile.UpdatedAtUtc = prep.UpdatedAtUtc;
            profile.UpdatedBy = prep.UpdatedBy; profile.ImportSourceReference = package.PackageId;
        }
        foreach (var decision in source.Decisions)
        {
            var key = DecisionPrefix + decision.SourceKey;
            if (await db.ClientAcceptanceDecisions.AnyAsync(x => x.ClientId == client.Id && x.ImportSourceReference == key, cancellationToken)) continue;
            db.ClientAcceptanceDecisions.Add(new() { ClientId = client.Id, Decision = decision.Decision, ContentHash = decision.ContentHash,
                SnapshotJson = decision.SnapshotJson, Reason = decision.Reason, DecidedBy = decision.DecidedBy, DecidedByUserId = decision.DecidedByUserId,
                GovernanceRoleAssignmentId = decision.GovernanceRoleAssignmentId, DecidedAtUtc = decision.DecidedAtUtc, ImportSourceReference = key });
        }
        foreach (var previous in await db.ClientCodexReviewRequests.Include(x => x.Task).Where(x => x.ClientId == client.Id && x.Status != "Superseded").ToListAsync(cancellationToken))
        {
            previous.Status = "Superseded";
            if (previous.Task.Status != ComplianceStatuses.Closed) { previous.Task.Status = ComplianceStatuses.Withdrawn; previous.Task.ClosureReason = "Replaced by an explicitly applied source acceptance revision."; }
        }
        await db.SaveChangesAsync(cancellationToken);
        var model = await ClientOnboardingService.BuildAsync(db, client.Id);
        var recipients = await (from user in db.Users where user.IsApproved join link in db.UserRoles on user.Id equals link.UserId
            join role in db.Roles on link.RoleId equals role.Id where role.Name == KcasRoles.ComplianceAdministrator || role.Name == KcasRoles.ComplianceApprover
            select user.Id).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var task = new ComplianceTask { ClientId = client.Id, TaskType = ComplianceTaskTypes.CodexReview, Owner = ComplianceWorkService.ComplianceReviewAudience,
            Title = $"Transferred client acceptance: {client.DisplayName}", Description = ClientOnboardingService.Brief(model),
            LinkedEntityType = nameof(ClientCodexReviewRequest), Priority = "High", UpdatedBy = actor };
        var request = new ClientCodexReviewRequest { ClientId = client.Id, Task = task, MaterialHash = model.MaterialHash,
            Brief = task.Description, RecipientUserIdsJson = JsonSerializer.Serialize(recipients, Json), ImportSourceReference = package.PackageId };
        db.ClientCodexReviewRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        model = await ClientOnboardingService.BuildAsync(db, client.Id);
        var latest = source.Reviews.SingleOrDefault(x => x.SourceKey == source.LatestReviewKey);
        var completed = source.CurrentResultsValidated && model.IsReady;
        if (completed)
        {
            request.Status = "ResultsRecorded"; request.CompletedContentHash = model.ContentHash; request.CompletedAtUtc = DateTime.UtcNow;
            request.CompletionSummary = latest?.CompletionSummary ?? "Transferred source KI decision and supporting findings. Review the recorded source snapshot before live KI confirmation.";
            task.Status = ComplianceStatuses.Closed; task.ClosedAtUtc = DateTime.UtcNow; task.ClosedBy = actor;
            task.ClosureReason = "Existing source findings mapped and current live prerequisites verified during import; KI confirmation remains separate.";
        }
        else
        {
            task.Status = ComplianceWorkStatuses.Open;
            task.Description = request.Brief = ClientOnboardingService.Brief(model);
        }
        await db.SaveChangesAsync(cancellationToken);
        var receipt = new ClientOnboardingImportReceipt { PackageId = package.PackageId, SourceEnvironment = package.SourceEnvironment,
            SourceExportedAtUtc = package.CreatedAtUtc, ImportedAtUtc = DateTime.UtcNow, ImportedBy = actor, Source = source,
            EvidenceIds = source.EvidenceReferences.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x => x, x => evidence[x].Id, StringComparer.OrdinalIgnoreCase),
            AppliedFingerprint = await LocalFingerprintAsync(db, client.Id, cancellationToken) };
        db.ComplianceAuditEvents.Add(new() { EntityType = nameof(ClientOnboardingProfile), EntityId = client.Id,
            Action = "ClientAcceptanceSourceImported", UserName = actor, Reason = reason,
            NewValueJson = JsonSerializer.Serialize(new { package.PackageId, source.SourceKey, source.RevisionHash, SourceDecisions = source.Decisions.Count,
                SourceHandoffs = source.Reviews.Count, LivePrerequisitesValidated = completed, CurrentRecipients = completed ? [] : recipients }, Json) });
        return receipt;
    }

    public static async Task<ClientOnboardingImportReceipt?> LoadReceiptAsync(ApplicationDbContext db, int clientId, CancellationToken cancellationToken = default)
    {
        var summaries = await db.ClientReviewTransferRecords.AsNoTracking().Where(x => x.ClientId == clientId &&
            x.Direction == ClientReviewTransferDirections.Incoming && x.Status == ClientReviewTransferStatuses.Applied)
            .OrderByDescending(x => x.Id).Select(x => x.SummaryJson).ToListAsync(cancellationToken);
        foreach (var summary in summaries)
        {
            try
            {
                using var json = JsonDocument.Parse(summary);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("onboardingReceipt", out var receipt) && receipt.ValueKind == JsonValueKind.Object)
                    return receipt.Deserialize<ClientOnboardingImportReceipt>(Json);
            }
            catch (JsonException) { /* Older receipts may have no structured summary. */ }
        }
        return null;
    }

    public static async Task<string> LocalFingerprintAsync(ApplicationDbContext db, int clientId, CancellationToken cancellationToken)
    {
        var profile = await db.ClientOnboardingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
        var requests = await db.ClientCodexReviewRequests.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.MaterialHash, x.Brief, x.RecipientUserIdsJson, x.CreatedAtUtc, x.CompletedAtUtc,
                x.CompletedContentHash, x.CompletionSummary, x.ImportSourceReference, TaskStatus = x.Task.Status, x.Task.ClosedAtUtc, x.Task.ClosedBy }).ToListAsync(cancellationToken);
        var decisions = await db.ClientAcceptanceDecisions.AsNoTracking().Where(x => x.ClientId == clientId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Decision, x.ContentHash, x.SnapshotJson, x.Reason, x.DecidedByUserId, x.DecidedBy,
                x.GovernanceRoleAssignmentId, x.DecidedAtUtc, x.ImportSourceReference }).ToListAsync(cancellationToken);
        return Hash(new { Preparation = profile is null ? null : Preparation(profile), profile?.ImportSourceReference, requests, decisions });
    }

    private static ClientOnboardingPreparationPackage Preparation(ClientOnboardingProfile p) => new() { Version = p.Version,
        RequestedService = p.RequestedService, ResponsibleRepresentative = p.ResponsibleRepresentative, PurposeAndProposedFunds = p.PurposeAndProposedFunds,
        DisclosureVersion = p.DisclosureVersion, DisclosureDeliveredAtUtc = p.DisclosureDeliveredAtUtc, DisclosureDeliveryReference = p.DisclosureDeliveryReference,
        EnhancedMeasures = p.EnhancedMeasures, RelationshipCommencedAtUtc = p.RelationshipCommencedAtUtc, RelationshipAuthorityReference = p.RelationshipAuthorityReference,
        UpdatedAtUtc = p.UpdatedAtUtc, UpdatedBy = p.UpdatedBy };
    internal static string RevisionHash(ClientOnboardingTransferPackage p) => Hash(new { p.SourceKey, p.SourceMaterialHash, p.SourceContentHash,
        p.CurrentResultsValidated, p.LatestReviewKey, p.Preparation, p.Reviews, p.Decisions, EvidenceReferences = p.EvidenceReferences.OrderBy(x => x.Key) });
    private static string Hash(object value) => TextHash(JsonSerializer.Serialize(value, Json));
    private static string TextHash(string? value) => value is null ? throw new ValidationException("Source snapshot text is missing.") : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool IsHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool ValidTime(DateTime value) => value != default && value <= DateTime.UtcNow.AddMinutes(1);
    private sealed class TimestampConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc).ToString("O"));
    }
}
