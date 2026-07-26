using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientVerificationItem
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [MaxLength(64)]
    public string FieldCode { get; set; } = "";

    [MaxLength(191)]
    public string FieldLabel { get; set; } = "";

    [MaxLength(32)]
    public string ChangeType { get; set; } = ClientVerificationChangeTypes.ConfirmExisting;

    public string? ExistingValue { get; set; }
    public string? ProposedValue { get; set; }

    [MaxLength(1024)]
    public string SourceReference { get; set; } = "";

    public string? Recommendation { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ClientVerificationStatuses.Pending;

    public bool IsBlocking { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(191)]
    public string CreatedBy { get; set; } = "";

    public DateTime? DecidedAtUtc { get; set; }

    [MaxLength(191)]
    public string? DecidedBy { get; set; }

    [MaxLength(1000)]
    public string? DecisionReason { get; set; }

    public DateTime? AppliedAtUtc { get; set; }

    [MaxLength(191)]
    public string? AppliedBy { get; set; }
}

public static class ClientVerificationStatuses
{
    public const string Pending = "Pending";
    public const string Verified = "Verified";
    public const string Rejected = "Rejected";
}

public static class ClientVerificationChangeTypes
{
    public const string ConfirmExisting = "ConfirmExisting";
    public const string Replace = "Replace";
}

public static class ClientVerificationFields
{
    public const string KanaanId = "Client.KanaanId";
    public const string Title = "Client.Title";
    public const string Initials = "Client.Initials";
    public const string FullName = "Client.FullName";
    public const string SurnameOrEntityName = "Client.SurnameOrEntityName";
    public const string DisplayName = "Client.DisplayName";
    public const string Language = "Client.Language";
    public const string ClientFolder = "Client.ClientFolder";
    public const string ClientCategory = "Client.ClientCategory";

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KanaanId] = "Kanaan ID",
            [Title] = "Title",
            [Initials] = "Initials",
            [FullName] = "Full name",
            [SurnameOrEntityName] = "Surname or entity name",
            [DisplayName] = "Display name",
            [Language] = "Language",
            [ClientFolder] = "Client folder",
            [ClientCategory] = "Client category"
        };
}

public sealed record ClientLifecycleReviewRequest(string Status, int? DuplicateOfClientId, string Reason);

public sealed record ClientVerificationCreateRequest(
    string FieldCode,
    string ChangeType,
    string? ProposedValue,
    string SourceReference,
    string Recommendation,
    bool IsBlocking);

public sealed record ClientOperationalPortfolioItem(
    int ClientId,
    string? KanaanId,
    string DisplayName,
    string LifecycleStatus,
    int PendingVerificationCount,
    int BlockingVerificationCount);

public sealed class ClientOperationalReviewModel
{
    public required Client Client { get; init; }
    public required IReadOnlyList<ClientVerificationItem> VerificationItems { get; init; }
    public int PendingCount => VerificationItems.Count(item => item.Status == ClientVerificationStatuses.Pending);
    public int BlockingCount => VerificationItems.Count(item =>
        item.Status == ClientVerificationStatuses.Pending && item.IsBlocking);
}
