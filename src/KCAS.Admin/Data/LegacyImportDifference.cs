using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class LegacyImportDifference
{
    public long Id { get; set; }

    public long LegacyImportRowStateId { get; set; }

    public LegacyImportRowState LegacyImportRowState { get; set; } = null!;

    [MaxLength(191)]
    public string FieldName { get; set; } = string.Empty;

    public string? BaselineValue { get; set; }

    public string? IncomingValue { get; set; }

    [MaxLength(32)]
    public string Decision { get; set; } = LegacyImportDecisionStatuses.Pending;

    public string? ResolvedValue { get; set; }

    [MaxLength(191)]
    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? ReviewReason { get; set; }
}

public static class LegacyImportDecisionStatuses
{
    public const string Pending = "Pending";
    public const string AcceptLegacy = "AcceptLegacy";
    public const string RetainKcas = "RetainKcas";
    public const string Corrected = "Corrected";
    public const string Deferred = "Deferred";
    public const string Rejected = "Rejected";
}
