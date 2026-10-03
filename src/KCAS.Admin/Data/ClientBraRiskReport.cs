using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace KCAS.Admin.Data;

public sealed class ClientBraRiskReport
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(64)] public string SourceContentHash { get; set; } = "";
    [MaxLength(64)] public string MethodVersion { get; set; } = ClientBraRiskMethod.Version;
    public string BraReference { get; set; } = "";
    public string ContentJson { get; set; } = "";
    [MaxLength(191)] public string PerformedBy { get; set; } = "";
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string? ImportPackageId { get; set; }
    public ClientBraRiskContent ReadContent() => JsonSerializer.Deserialize<ClientBraRiskContent>(ContentJson)
        ?? throw new InvalidOperationException("BRA-linked report content is missing.");
}

public sealed class ClientBraRiskContent
{
    public string Scope { get; set; } = "";
    public string Limitations { get; set; } = "";
    public List<ClientBraRiskScenario> Scenarios { get; set; } = [];
}

public sealed class ClientBraRiskScenario
{
    public string RiskType { get; set; } = "";
    public string Scenario { get; set; } = "";
    public int? InherentLikelihood { get; set; }
    public int? InherentImpact { get; set; }
    public string InherentLikelihoodReason { get; set; } = "";
    public string InherentImpactReason { get; set; } = "";
    public string ControlsAndEffect { get; set; } = "";
    public List<ClientBraControlEvidence> Evidence { get; set; } = [];
    public int? ResidualLikelihood { get; set; }
    public int? ResidualImpact { get; set; }
    public string ResidualLikelihoodReason { get; set; } = "";
    public string ResidualImpactReason { get; set; } = "";
    public string ImpactReductionJustification { get; set; } = "";
    public int? InherentScore => ClientBraRiskMethod.Score(InherentLikelihood, InherentImpact);
    public int? ResidualScore => ClientBraRiskMethod.Score(ResidualLikelihood, ResidualImpact);
}

public sealed class ClientBraControlEvidence
{
    public int EvidenceItemId { get; set; }
    public string EvidenceKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string EvidenceType { get; set; } = "";
    public string? RelativePath { get; set; }
    public string? FileSha256 { get; set; }
    public string? PerformedBy { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
    public string? Findings { get; set; }
}

public static class ClientBraRiskMethod
{
    public const string Version = "BRA-client-linkage-proposal-v1";
    public static readonly string[] RiskTypes = ["ML", "TF", "PF"];
    public static int? Score(int? likelihood, int? impact) => likelihood is >= 1 and <= 3 && impact is >= 1 and <= 3 ? likelihood * impact : null;
    public static string Rating(int? score) => score switch { 1 or 2 => "Low", 3 or 4 => "Moderate", 6 or 9 => "High", _ => "Not assessed" };
    public static string Display(string type) => type switch { "ML" => "Money laundering", "TF" => "Terrorist financing", "PF" => "Proliferation financing", _ => type };
    public static void Validate(ClientBraRiskContent content)
    {
        if (content is null || content.Scenarios is null || content.Scenarios.Any(x => x is null || x.Evidence is null))
            throw new ValidationException("BRA-linked scenario content is missing.");
        Require(content.Scope, "Review scope");
        Require(content.Limitations, "Limitations or statement that none were identified");
        if (content.Scenarios.Count != 3 || !content.Scenarios.Select(x => x.RiskType).Order().SequenceEqual(RiskTypes.Order()))
            throw new ValidationException("Record one distinct scenario for each of ML, TF and PF.");
        foreach (var row in content.Scenarios)
        {
            Require(row.Scenario, $"{row.RiskType} scenario");
            if (row.InherentScore is null || row.ResidualScore is null)
                throw new ValidationException("Each likelihood and impact must be a whole number from 1 to 3.");
            Require(row.InherentLikelihoodReason, "Inherent likelihood reason");
            Require(row.InherentImpactReason, "Inherent impact reason");
            Require(row.ControlsAndEffect, "Implemented controls and their effect");
            Require(row.ResidualLikelihoodReason, "Residual likelihood reason");
            Require(row.ResidualImpactReason, "Residual impact reason");
            if (row.Evidence.Count == 0 || row.Evidence.Any(x => x is null || string.IsNullOrWhiteSpace(x.EvidenceKey)) ||
                row.Evidence.Select(x => x.EvidenceKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != row.Evidence.Count)
                throw new ValidationException("Link distinct actual evidence supporting each scenario's implemented controls.");
            if (row.ResidualImpact < row.InherentImpact)
                Require(row.ImpactReductionJustification, "Separate consequence-limiting justification for reducing impact");
        }
    }
    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 20000) throw new ValidationException($"{label} is required (maximum 20000 characters).");
    }
}

public sealed class ClientBraRiskEdit
{
    public int? PreviousReportId { get; set; }
    public string SourceContentHash { get; set; } = "";
    public string BraReference { get; set; } = "";
    public string PerformedBy { get; set; } = "Human reviewer";
    public ClientBraRiskContent Content { get; set; } = new() { Scenarios = ClientBraRiskMethod.RiskTypes.Select(type => new ClientBraRiskScenario { RiskType = type }).ToList() };
}

public sealed record ClientBraRiskModel(ClientOnboardingModel ClientReview, ClientBraRiskReport? Report,
    IReadOnlyList<ClientBraRiskReport> History, bool CanPrepare)
{
    public bool IsCurrent => Report is { ImportPackageId: null } && Report.SourceContentHash == ClientReview.ChecksContentHash;
}
