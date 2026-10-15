using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InsurancePolicy
{
    public int Id { get; set; }
    public Guid TransferKey { get; set; } = Guid.NewGuid();
    [MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    [Required, MaxLength(240)] public string InsuredName { get; set; } = "";
    [Required, MaxLength(120)] public string PolicyNumber { get; set; } = "";
    [Required, MaxLength(240)] public string Insurer { get; set; } = "";
    [MaxLength(240)] public string Broker { get; set; } = "";
    [MaxLength(500)] public string BrokerContact { get; set; } = "";
    [MaxLength(500)] public string PrincipalAddress { get; set; } = "";
    [MaxLength(2000)] public string BusinessDescription { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddYears(1).AddDays(-1);
    public DateOnly? RetroactiveDate { get; set; }
    public decimal Limit { get; set; }
    public bool CombinedLimit { get; set; } = true;
    public decimal Excess { get; set; }
    public decimal PremiumExVat { get; set; }
    public decimal Vat { get; set; }
    public decimal? MonthlyInstalment { get; set; }
    [MaxLength(32)] public string PaymentFrequency { get; set; } = "Unconfirmed";
    [MaxLength(8000)] public string CoverSummary { get; set; } = "";
    [MaxLength(8000)] public string Exclusions { get; set; } = "";
    [MaxLength(8000)] public string Findings { get; set; } = "";
    [MaxLength(500)] public string Owner { get; set; } = ComplianceWorkService.ComplianceReviewAudience;
    public int ReminderDays { get; set; } = 60;
    public int FollowUpDays { get; set; } = 30;
    public int UrgentDays { get; set; } = 7;
    [MaxLength(32)] public string Status { get; set; } = InsuranceStages.Preparation;
    [MaxLength(32)] public string? RequestedVersion { get; set; }
    [MaxLength(32)] public string? ReviewedVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string UpdatedBy { get; set; } = "";
    public List<InsuranceDocument> Documents { get; set; } = [];
    public List<InsuranceEvent> Events { get; set; } = [];
}

public sealed class InsuranceDocument
{
    public int Id { get; set; }
    public int InsurancePolicyId { get; set; }
    public Guid TransferKey { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string Kind { get; set; } = "Quotation";
    [MaxLength(240)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string RelativePath { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public DateOnly? DocumentDate { get; set; }
}

public sealed class InsuranceEvent
{
    public int Id { get; set; }
    public int InsurancePolicyId { get; set; }
    public Guid TransferKey { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string Action { get; set; } = "";
    [MaxLength(191)] public string Performer { get; set; } = "";
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public DateOnly OccurredOn { get; set; }
    public string Summary { get; set; } = "";
    public Guid? DocumentKey { get; set; }
}

public static class InsuranceStages
{
    public const string Preparation = "Preparation";
    public const string Submitted = "Submitted";
    public const string QuotationReceived = "QuotationReceived";
    public const string Reviewed = "Reviewed";
    public const string Accepted = "Accepted";
    public const string Confirmed = "CoverConfirmed";
    public static readonly string[] All = [Preparation, Submitted, QuotationReceived, Reviewed, Accepted, Confirmed];
    public static readonly string[] DocumentKinds = ["Quotation", "SignedProposal", "NoClaimsDeclaration", "FinancialStatements", "Correspondence", "Acceptance", "PolicySchedule", "PolicyWording", "InsurerConfirmation", "Other"];
    public static string Display(string stage) => stage switch { QuotationReceived => "Quotation received", Confirmed => "Cover confirmed", _ => stage };
}

internal static class InsuranceModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<InsurancePolicy>(e =>
        {
            e.HasIndex(x => x.TransferKey).IsUnique();
            e.HasIndex(x => new { x.PolicyNumber, x.StartDate }).IsUnique();
            e.Property(x => x.Version).IsConcurrencyToken();
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.StartDate));
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.EndDate));
            ApplicationDbContext.ConfigureDateOnly(e.Property(x => x.RetroactiveDate));
            e.Property(x => x.CoverSummary).HasColumnType("longtext");
            e.Property(x => x.Exclusions).HasColumnType("longtext");
            e.Property(x => x.Findings).HasColumnType("longtext");
            foreach (var name in new[] { nameof(InsurancePolicy.Limit), nameof(InsurancePolicy.Excess), nameof(InsurancePolicy.PremiumExVat), nameof(InsurancePolicy.Vat), nameof(InsurancePolicy.MonthlyInstalment) })
                e.Property(name).HasPrecision(18, 2);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.InsurancePolicyId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.InsurancePolicyId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<InsuranceDocument>().HasIndex(x => x.TransferKey).IsUnique();
        ApplicationDbContext.ConfigureDateOnly(builder.Entity<InsuranceDocument>().Property(x => x.DocumentDate));
        builder.Entity<InsuranceEvent>().HasIndex(x => x.TransferKey).IsUnique();
        ApplicationDbContext.ConfigureDateOnly(builder.Entity<InsuranceEvent>().Property(x => x.OccurredOn));
        builder.Entity<ComplianceTask>().HasIndex(x => x.InsurancePolicyId).IsUnique();
        builder.Entity<ComplianceTask>().HasOne<InsurancePolicy>().WithMany().HasForeignKey(x => x.InsurancePolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}
