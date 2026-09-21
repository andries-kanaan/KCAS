using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientAdviceCase
{
    public int Id { get; set; }
    [MaxLength(36)] public string? TransferKey { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int? PreviousAdviceCaseId { get; set; }
    public ClientAdviceCase? PreviousAdviceCase { get; set; }

    [MaxLength(48)] public string AdviceType { get; set; } = ClientAdviceTypes.NewInvestment;
    [MaxLength(64)] public string RiskMethodologyCode { get; set; } = ClientAdviceMethodologies.KcasEvidenced;
    [MaxLength(32)] public string Status { get; set; } = ClientAdviceStatuses.Draft;
    public int Revision { get; set; } = 1;
    public DateOnly AdviceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [MaxLength(191)] public string AdviserName { get; set; } = "";
    [MaxLength(191)] public string PreparedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string AdviceScope { get; set; } = "";
    public string MeetingSummary { get; set; } = "";
    public string NeedsAndObjectives { get; set; } = "";
    public string FinancialSituation { get; set; } = "";
    public string AdviceLimitations { get; set; } = "";
    public string ProductKnowledgeSummary { get; set; } = "";
    public decimal? InvestmentAmount { get; set; }
    public decimal? InvestmentPortfolioPercent { get; set; }
    public decimal? DomesticPreferencePercent { get; set; }
    public decimal? OffshorePreferencePercent { get; set; }

    public int? CalculatedRiskScore { get; set; }
    [MaxLength(48)] public string? CalculatedRiskLevel { get; set; }
    [MaxLength(48)] public string? FinalRiskLevel { get; set; }
    public string? RiskOverrideReason { get; set; }

    public string RecommendationSummary { get; set; } = "";
    public string RecommendationRationale { get; set; } = "";
    public string CostsAndFees { get; set; } = "";
    public string TaxConsequences { get; set; } = "";
    public string LiquidityAndRestrictions { get; set; } = "";
    public string MaterialRisks { get; set; } = "";
    public bool IsReplacement { get; set; }
    public string ReplacementConsequences { get; set; } = "";
    public string ClientDeparture { get; set; } = "";
    public string WarningsGiven { get; set; } = "";

    public string? FrozenSnapshotJson { get; set; }
    [MaxLength(64)] public string? FrozenSnapshotSha256 { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<ClientAdviceParticipant> Participants { get; } = [];
    public ICollection<ClientAdviceRiskResponse> RiskResponses { get; } = [];
    public ICollection<ClientAdviceProduct> Products { get; } = [];
    public ICollection<ClientAdviceFactSource> FactSources { get; } = [];
    public ICollection<ClientAdviceInvestmentLink> InvestmentLinks { get; } = [];
    public ICollection<ClientAdviceReviewFinding> ReviewFindings { get; } = [];
    public ICollection<ClientAdviceApproval> Approvals { get; } = [];
    public ICollection<ClientAdviceDocument> Documents { get; } = [];
}

public sealed class ClientAdviceFactSource
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    [MaxLength(96)] public string FactName { get; set; } = "";
    public DateOnly? SourceDate { get; set; }
    [MaxLength(512)] public string DocumentPath { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed class ClientAdviceInvestmentLink
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    public int ClientInvestmentAccountId { get; set; }
    public ClientInvestmentAccount InvestmentAccount { get; set; } = null!;
    [MaxLength(48)] public string Role { get; set; } = "ExistingPortfolio";
}

public sealed class ClientAdviceParticipant
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(48)] public string Role { get; set; } = "AdviceSubject";
}

public sealed class ClientAdviceRiskResponse
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    [MaxLength(48)] public string QuestionCode { get; set; } = "";
    [MaxLength(96)] public string AnswerCode { get; set; } = "";
    public int Score { get; set; }
    public string? Explanation { get; set; }
}

public sealed class ClientAdviceProduct
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    [MaxLength(191)] public string ProductName { get; set; } = "";
    [MaxLength(191)] public string? Provider { get; set; }
    [MaxLength(96)] public string? ProductType { get; set; }
    public bool IsRecommended { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AllocationPercent { get; set; }
    public string? Motivation { get; set; }
    [MaxLength(512)] public string? SupportingDocumentPath { get; set; }
}

public sealed class ClientAdviceReviewFinding
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    [MaxLength(32)] public string Severity { get; set; } = ClientAdviceFindingSeverities.Medium;
    [MaxLength(96)] public string Category { get; set; } = "";
    [MaxLength(96)] public string? AffectedField { get; set; }
    public string Finding { get; set; } = "";
    public string? EvidenceReference { get; set; }
    public string? RecommendedCorrection { get; set; }
    [MaxLength(32)] public string Status { get; set; } = ClientAdviceFindingStatuses.Open;
    public string? Resolution { get; set; }
    [MaxLength(191)] public string PerformedBy { get; set; } = "";
    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public sealed class ClientAdviceApproval
{
    public int Id { get; set; }
    public int ClientAdviceCaseId { get; set; }
    public ClientAdviceCase AdviceCase { get; set; } = null!;
    [MaxLength(191)] public string Reviewer { get; set; } = "";
    [MaxLength(32)] public string Decision { get; set; } = "Approved";
    public string Reason { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientAdviceDocument
{
    public int Id { get; set; }
    public int? ClientAdviceCaseId { get; set; }
    public ClientAdviceCase? AdviceCase { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(48)] public string DocumentType { get; set; } = ClientAdviceDocumentTypes.GeneratedAdviceRecord;
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(512)] public string? SourcePath { get; set; }
    [MaxLength(64)] public string? FileSha256 { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? FileLastWriteTimeUtc { get; set; }
    [MaxLength(191)] public string? RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ClientAdviceStatuses
{
    public const string Draft = "Draft";
    public const string ReadyForReview = "ReadyForReview";
    public const string Returned = "Returned";
    public const string ApprovedForIssue = "ApprovedForIssue";
    public const string Issued = "Issued";
    public const string Complete = "Complete";
    public const string Superseded = "Superseded";
    public const string Cancelled = "Cancelled";
}

public static class ClientAdviceTypes
{
    public const string NewInvestment = "NewInvestment";
    public const string TopUp = "TopUp";
    public const string Switch = "Switch";
    public const string RetirementDecision = "RetirementDecision";
    public const string Replacement = "Replacement";
    public const string AnnualReview = "AnnualReview";
    public const string Other = "Other";
    public static readonly string[] All = [NewInvestment, TopUp, Switch, RetirementDecision, Replacement, AnnualReview, Other];
}

public static class ClientAdviceMethodologies
{
    public const string KcasEvidenced = "KCAS_EVIDENCED_V1";
    public const string PreviousFormCorrectedBands = "PREVIOUS_FORM_CORRECTED_BANDS_V1";
}

public static class ClientAdviceFindingStatuses
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
    public const string Accepted = "Accepted";
    public const string AwaitingClientConfirmation = "AwaitingClientConfirmation";
}

public static class ClientAdviceFindingSeverities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";
    public static readonly string[] All = [Low, Medium, High, Critical];
}

public static class ClientAdviceDocumentTypes
{
    public const string HistoricalRiskAnalyser = "HistoricalRiskAnalyser";
    public const string HistoricalAdviceRecord = "HistoricalAdviceRecord";
    public const string GeneratedAdviceRecord = "GeneratedAdviceRecord";
    public const string SignedAdviceRecord = "SignedAdviceRecord";
}
