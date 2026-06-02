using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientKycRecommendation
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int? ClientKycPolicyId { get; set; }

    public ClientKycPolicy? KycPolicy { get; set; }

    public int? LegacyRecommendationId { get; set; }

    public int? LegacyClientId { get; set; }

    [MaxLength(256)]
    public string? KanaanId { get; set; }

    [MaxLength(256)]
    public string? RecommendationType { get; set; }

    [MaxLength(256)]
    public string? Status { get; set; }

    public DateOnly? RecommendationDate { get; set; }

    public string? Details { get; set; }

    public string? Outcome { get; set; }

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
