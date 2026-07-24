using System.Text.RegularExpressions;

namespace KCAS.Admin.Data;

public static partial class ClientCategoryInference
{
    public static ClientCategoryInferenceResult InferFromLegacyClient(
        string? surnameOrEntityName,
        string? fullName,
        string? displayName,
        string? clientFolder)
    {
        var folderName = FinalPathSegment(clientFolder);
        var text = NormalizeForCategory($"{surnameOrEntityName} {fullName} {displayName} {folderName}");
        return Infer(text, ClientCategorySources.LegacyImportInferred);
    }

    public static ClientCategoryInferenceResult? InferFromEvidence(string? relativePath, string? evidenceType)
    {
        var text = NormalizeForCategory(relativePath);
        if (string.Equals(evidenceType, "TrustDeed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidenceType, "TrustParties", StringComparison.OrdinalIgnoreCase) ||
            TrustEvidenceRegex().IsMatch(text))
        {
            return new(ClientCategories.Trust, ClientCategorySources.EvidenceScanInferred, "Evidence scan found trust deed, trustee/founder, Master or letters-of-authority evidence.");
        }

        if (string.Equals(evidenceType, "LegalPersonRegistration", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidenceType, "LegalPersonControllers", StringComparison.OrdinalIgnoreCase) ||
            LegalPersonEvidenceRegex().IsMatch(text))
        {
            return new(ClientCategories.LegalPerson, ClientCategorySources.EvidenceScanInferred, "Evidence scan found company, close-corporation, CIPC, director or member authority evidence.");
        }

        if (EstateRegex().IsMatch(text))
        {
            return new(ClientCategories.Other, ClientCategorySources.EvidenceScanInferred, "Evidence scan found deceased-estate or proposed-distribution evidence.");
        }

        return null;
    }

    public static bool CanApplyInferredCategory(Client client) =>
        !string.Equals(client.ClientCategorySource, ClientCategorySources.Manual, StringComparison.OrdinalIgnoreCase);

    private static ClientCategoryInferenceResult Infer(string text, string source)
    {
        if (TrustLegacyRegex().IsMatch(text))
        {
            return new(ClientCategories.Trust, source, "Legacy client name or folder contains trust indicators.");
        }

        if (LegalPersonLegacyRegex().IsMatch(text))
        {
            return new(ClientCategories.LegalPerson, source, "Legacy client name or folder contains company or legal-entity indicators.");
        }

        if (EstateRegex().IsMatch(text))
        {
            return new(ClientCategories.Other, source, "Legacy client name or folder contains deceased-estate or proposed-distribution indicators.");
        }

        return new(ClientCategories.NaturalPerson, source, "No trust, legal-person or estate indicators were found in the legacy client name or folder.");
    }

    private static string NormalizeForCategory(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.ToLowerInvariant()
                .Replace("\\", " ", StringComparison.Ordinal)
                .Replace("/", " ", StringComparison.Ordinal)
                .Replace("_", " ", StringComparison.Ordinal)
                .Replace("-", " ", StringComparison.Ordinal);

    private static string? FinalPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim().TrimEnd('\\', '/');
        var lastSlash = normalized.LastIndexOfAny(['\\', '/']);
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    [GeneratedRegex(@"\b(trust|familie\s+trust|family\s+trust|testamentere\s+trust|testamentary\s+trust)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrustLegacyRegex();

    [GeneratedRegex(@"\b(pty|ltd|limited|cc|close\s+corporation|nominees|company|holdings|properties|beleggings|investments)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LegalPersonLegacyRegex();

    [GeneratedRegex(@"\b(estate|estate\s+late|late\s+estate|boedel|wyle|deceased|voorgestelde|bateverdeling|bate\s+verdeling|executorship|executor|liquidation\s+and\s+distribution)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EstateRegex();

    [GeneratedRegex(@"\b(trust\s+deed|trustdeed|trustakte|letters?\s+of\s+authority|lettersofauthority|master\s+of\s+the\s+high\s+court|trust\s+registration|trustees?|founder)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrustEvidenceRegex();

    [GeneratedRegex(@"\b(cipc|company\s+registration|companyregistration|cor14|cor39|ck1|ck2|director|directors|member|members|resolution|authorised\s+signatory|authorized\s+signatory)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LegalPersonEvidenceRegex();
}

public sealed record ClientCategoryInferenceResult(string Category, string Source, string Reason);
