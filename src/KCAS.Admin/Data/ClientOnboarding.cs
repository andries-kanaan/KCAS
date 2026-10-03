using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientOnboardingProfile
{
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(64)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestedService { get; set; } = "";
    public string ResponsibleRepresentative { get; set; } = "";
    public string PurposeAndProposedFunds { get; set; } = "";
    public string DisclosureVersion { get; set; } = "";
    public DateTime? DisclosureDeliveredAtUtc { get; set; }
    public string DisclosureDeliveryReference { get; set; } = "";
    public string EnhancedMeasures { get; set; } = "";
    public DateTime? RelationshipCommencedAtUtc { get; set; }
    public string RelationshipAuthorityReference { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string UpdatedBy { get; set; } = "";
}

public sealed class ClientCodexReviewRequest
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int ComplianceTaskId { get; set; }
    public ComplianceTask Task { get; set; } = null!;
    [MaxLength(64)] public string MaterialHash { get; set; } = "";
    [MaxLength(32)] public string Status { get; set; } = "AwaitingCodex";
    public string Brief { get; set; } = "";
    public string RecipientUserIdsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    [MaxLength(64)] public string? CompletedContentHash { get; set; }
    public string? CompletionSummary { get; set; }
}

public sealed class ClientAcceptanceDecision
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(32)] public string Decision { get; set; } = "ReturnForClarification";
    [MaxLength(64)] public string ContentHash { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string Reason { get; set; } = "";
    [MaxLength(64)] public string DecidedByUserId { get; set; } = "";
    [MaxLength(191)] public string DecidedBy { get; set; } = "";
    public int GovernanceRoleAssignmentId { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ImportSourceReference { get; set; }
}

public sealed class ClientOnboardingEdit
{
    public string? Version { get; set; }
    public string RequestedService { get; set; } = "";
    public string ResponsibleRepresentative { get; set; } = "";
    public string PurposeAndProposedFunds { get; set; } = "";
    public string DisclosureVersion { get; set; } = "";
    public DateTime? DisclosureDeliveredAtUtc { get; set; }
    public string DisclosureDeliveryReference { get; set; } = "";
    public string EnhancedMeasures { get; set; } = "";
}

public sealed class ClientOnboardingModel
{
    public Client Client { get; set; } = null!;
    public ClientOnboardingProfile? Profile { get; set; }
    public ClientEvidenceReadinessModel Evidence { get; set; } = new();
    public ClientRiskAssessment? Assessment { get; set; }
    public List<ClientAcceptanceDecision> Decisions { get; set; } = [];
    public ClientCodexReviewRequest? Request { get; set; }
    public List<string> CheckBlockers { get; set; } = [];
    public List<string> IntakeBlockers { get; set; } = [];
    public string ContentHash { get; set; } = "";
    public string MaterialHash { get; set; } = "";
    public bool CanPrepare { get; set; }
    public bool CanDecide { get; set; }
    public bool IsAccepted => Decisions.FirstOrDefault() is { Decision: "Accepted", ImportSourceReference: null } decision && decision.ContentHash == ContentHash && IsReady && Request?.Status != "AwaitingCodex";
    public bool IsReady => CheckBlockers.Count == 0 && IntakeBlockers.Count == 0;
    public string Status => !Client.RequiresClientAcceptance ? "Acceptance workflow not started"
        : IsAccepted ? "Accepted" : Decisions.FirstOrDefault()?.Decision == "Declined" ? "Declined"
        : Request?.Status == "AwaitingCodex" ? "Awaiting Codex review" : IsReady ? "Ready for KI decision" : "Preparation required";
}

public sealed record ClientCodexNotification(int ClientId, string ClientName, string Status, string Brief);
