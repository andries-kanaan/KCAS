using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class PerformanceFeeProfile
{
    public int Id { get; set; }
    public int ClientInvestmentAccountId { get; set; }
    public ClientInvestmentAccount Account { get; set; } = null!;
    [MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public DateOnly? InceptionDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public string PreparationJson { get; set; } = "{}";
    public string? CheckpointJson { get; set; }
    public string StatementReviewJson { get; set; } = "{}";
    [MaxLength(32)] public string? CodexRequestVersion { get; set; }
    [MaxLength(191)] public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PerformanceFeeClaim> Claims { get; set; } = [];
    public List<PerformanceFeeEvent> Events { get; set; } = [];
}

public sealed class PerformanceFeeClaim
{
    public int Id { get; set; }
    public int PerformanceFeeProfileId { get; set; }
    [MaxLength(40)] public string Reference { get; set; } = "PDF-" + Guid.NewGuid().ToString("N");
    [MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(32)] public string Status { get; set; } = FeeClaimStages.Draft;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string InputJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    [MaxLength(191)] public string PreparedBy { get; set; } = "";
    [MaxLength(32)] public string Performer { get; set; } = "Manual";
    public DateTime PreparedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public decimal Paid { get; set; }
}

public sealed class PerformanceFeeEvent
{
    public int Id { get; set; }
    public int PerformanceFeeProfileId { get; set; }
    public int? ClaimId { get; set; }
    [MaxLength(64)] public string Action { get; set; } = "";
    [MaxLength(32)] public string Performer { get; set; } = "Manual";
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public DateOnly OccurredOn { get; set; }
    public string Reference { get; set; } = "";
    public string Summary { get; set; } = "";
    public decimal? Amount { get; set; }
}

public static class FeeClaimStages
{
    public const string Draft = "Draft", Approved = "Approved", Submitted = "Submitted", Accepted = "Accepted",
        Deducted = "Deducted", Paid = "Paid", NoFee = "NoFee", Voided = "Voided", Returned = "Returned";
    public static bool Frozen(string status) => status != Draft && status != Voided && status != Returned;
    public static bool Exportable(string status) => status is Approved or Submitted or Accepted or Deducted or Paid;
}

public sealed class FeePreparation
{
    public const string SupportedMethod = "CapitalHwmAct365V1";
    public string Method { get; set; } = SupportedMethod;
    public bool MethodMatchesMandate { get; set; }
    public bool ZeroRateConfirmed { get; set; }
    public bool RequiresRebuild { get; set; }
    public string MandateReference { get; set; } = "";
    public DateOnly? MandateEffectiveFrom { get; set; }
    public string MethodRationale { get; set; } = "";
    public string WrapFund { get; set; } = "";
    public bool IsSouthAfricanWrap { get; set; }
    public string Administrator { get; set; } = "";
    public DateOnly? Inception { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public decimal? OpeningValue { get; set; }
    public decimal? OpeningBenchmark { get; set; }
    public decimal? OpeningHighWaterMark { get; set; }
    public string OpeningReference { get; set; } = "";
    public bool NoPriorFeeConfirmed { get; set; }
    public string PriorFeeReviewReference { get; set; } = "";
    public decimal? ClosingValue { get; set; }
    public string ClosingReference { get; set; } = "";
    public decimal? FeePercent { get; set; }
    public decimal? HurdleSpreadPercent { get; set; }
    public decimal? VatPercent { get; set; }
    public string VatReference { get; set; } = "";
    public bool CashFlowsComplete { get; set; }
    public string CashFlowReference { get; set; } = "";
    public string ScopeRationale { get; set; } = "";
    public List<FeeBenchmarkPeriod> Benchmarks { get; set; } = [];
    public List<FeeMovement> Movements { get; set; } = [];
    public List<FeeWrapPreparation> Wraps { get; set; } = [];
    public List<FeeScopeTransfer> Transfers { get; set; } = [];
    public List<string> EvidenceIssues { get; set; } = [];
    public List<FeeAccountDependency> AccountDependencies { get; set; } = [];
    public List<FeeTransferDecision> AccountTransferDecisions { get; set; } = [];
    public List<FeeScopeNote> OtherScopes { get; set; } = [];
    public List<int> MandateAccountIds { get; set; } = [];
    public string MandateCoverageReference { get; set; } = "";
    public bool UsePublishedCpi { get; set; }
    public List<string> AutomationNotes { get; set; } = [];
    public List<string> ReviewIssues { get; set; } = [];
    public int? MandateSourceAccountId { get; set; }
    public string MandateTermsSnapshot { get; set; } = "";
    public FeeStatementHistory? StatementHistory { get; set; }
    public List<FeeOpeningTransfer> OpeningTransfers { get; set; } = [];
    public bool UseSharedPolicy { get; set; }
    public FeeSharedPolicy? SharedPolicy { get; set; }
    public List<FeeReviewPoint> ReviewPoints { get; set; } = [];
    public List<FeeCapitalTrace> CapitalTraces { get; set; } = [];
}

public sealed record FeeOpeningTransfer(string Account, string Wrap, DateOnly Date, decimal Amount, decimal Benchmark, decimal HighWaterMark, string Reference);

public sealed record FeeScopeNote(string Code, string Name, decimal ClosingValue, string Reason);

public sealed record FeeAccountDependency(int AccountId, string PreparationHash);
public sealed record FeeTransferDecision(string Key, string Reason, string RecordedBy, DateTime RecordedAtUtc);

public sealed class FeeWrapPreparation
{
    public string Code { get; set; } = "";
    public FeePreparation Input { get; set; } = new();
}

public sealed class FeeScopeTransfer
{
    public DateOnly Date { get; set; }
    public string FromWrap { get; set; } = "";
    public string ToWrap { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? ValueBeforeTransfer { get; set; }
    public bool ContinuityConfirmed { get; set; }
    public string Reference { get; set; } = "";
}

public sealed class FeeBenchmarkPeriod
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal CpiPercent { get; set; }
    public string Reference { get; set; } = "";
}

public sealed class FeeMovement
{
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "Contribution";
    public decimal Amount { get; set; }
    public decimal? ValueBeforeWithdrawal { get; set; }
    public string Reference { get; set; } = "";
    public decimal? TransferredBenchmark { get; set; }
    public decimal? TransferredHighWaterMark { get; set; }
}

public sealed record FeeCheckpoint(DateOnly Date, decimal Value, decimal Benchmark, decimal HighWaterMark, string Reference)
{
    public List<FeeWrapCheckpoint> Wraps { get; init; } = [];
}
public sealed record FeeWrapCheckpoint(string Code, FeeCheckpoint Checkpoint);
public sealed record FeeWrapResult(string Code, string Name, FeeCalculationResult Result);
public sealed record FeeCalculationStep(DateOnly Date, string Description, decimal Benchmark, decimal HighWaterMark);
public sealed class FeeCalculationResult
{
    public string? StatementPreparationFingerprint { get; set; }
    public List<string> Issues { get; set; } = [];
    public List<FeeCalculationStep> Steps { get; set; } = [];
    public decimal Benchmark { get; set; }
    public decimal HighWaterMark { get; set; }
    public decimal Threshold { get; set; }
    public decimal Outperformance { get; set; }
    public decimal FeeExVat { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public FeeCheckpoint? NextCheckpoint { get; set; }
    public List<FeeWrapResult> Wraps { get; set; } = [];
    public bool IsZeroRateReview { get; set; }
    public bool HasCalculation { get; set; }
    public bool HasVatCalculation { get; set; }
    public List<string> CalculationIssues { get; set; } = [];
    public bool CanShowCalculation => HasCalculation || IsReady;
    public bool IsReady => Issues.Count == 0 && (NextCheckpoint is not null || IsZeroRateReview);
}

public static class FeeResultDescription
{
    public static string Describe(FeeCalculationResult result, decimal? rate = null)
    {
        if (!result.IsReady) return result.HasCalculation ? $"Calculated draft - review required ({result.Issues.Count})" : $"Calculation incomplete ({result.CalculationIssues.Count switch { 0 => result.Issues.Count, var n => n }})";
        if (result.IsZeroRateReview || rate == 0) return "Verified 0% mandate - no fee";
        if (result.Total > 0) return "Fee payable";
        if (result.Wraps.Count > 0) return result.Wraps.All(x => x.Result.IsZeroRateReview) ? "Verified 0% mandates - no fee" : "No fee payable - see separate wrap results";
        return result.Outperformance == 0 ? "No chargeable outperformance - no fee" : "Fee rounds to zero";
    }
}

public sealed class PerformanceFeeStatement
{
    public int Id { get; set; }
    public int PerformanceFeeProfileId { get; set; }
    [MaxLength(256)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string DocumentJson { get; set; } = "{}";
    [MaxLength(191)] public string ImportedBy { get; set; } = "";
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class FeeJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) where T : new() => JsonSerializer.Deserialize<T>(value, Options) ?? new();
}

internal static class PerformanceFeeModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<PerformanceFeeProfile>(e =>
        {
            e.HasIndex(x => x.ClientInvestmentAccountId).IsUnique();
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.ClientInvestmentAccountId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.PreparationJson).HasColumnType("longtext");
            e.Property(x => x.CheckpointJson).HasColumnType("longtext");
            e.Property(x => x.StatementReviewJson).HasColumnType("longtext");
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.InceptionDate));
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.NextReviewDate));
            e.HasMany(x => x.Claims).WithOne().HasForeignKey(x => x.PerformanceFeeProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.PerformanceFeeProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PerformanceFeeClaim>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.PerformanceFeeProfileId, x.From, x.To });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.InputJson).HasColumnType("longtext");
            e.Property(x => x.ResultJson).HasColumnType("longtext");
            e.Property(x => x.Paid).HasPrecision(18, 2);
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.From));
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.To));
        });
        builder.Entity<PerformanceFeeEvent>(e =>
        {
            e.Property(x => x.Reference).HasColumnType("longtext");
            e.Property(x => x.Summary).HasColumnType("longtext");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.OccurredOn));
        });
        builder.Entity<PerformanceFeeStatement>(e =>
        {
            e.HasOne<PerformanceFeeProfile>().WithMany().HasForeignKey(x => x.PerformanceFeeProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PerformanceFeeProfileId, x.Sha256 }).IsUnique();
            e.Property(x => x.Content).HasColumnType("longblob");
            e.Property(x => x.DocumentJson).HasColumnType("longtext");
        });
    }
}
