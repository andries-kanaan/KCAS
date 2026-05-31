using System.Globalization;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyKycImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static ClientKycPolicy Map(
        IReadOnlyDictionary<string, string?> row,
        int clientId,
        IReadOnlyDictionary<int, string> mainClassNamesById,
        IReadOnlyDictionary<int, string> subClassNamesById,
        DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy KYC row is missing a numeric id.");
        var mainClassId = ReadInt(row, "main_class");
        var subClassId = ReadInt(row, "sub_class");

        var isLifeAndDisabilityCover = subClassId == 29 || string.Equals(
            subClassId is not null && subClassNamesById.TryGetValue(subClassId.Value, out var resolvedSubClassName)
                ? resolvedSubClassName
                : Read(row, "sub_class"),
            "Life and Disability Cover",
            StringComparison.OrdinalIgnoreCase);

        return new ClientKycPolicy
        {
            ClientId = clientId,
            LegacyKycId = legacyId,
            LegacyClientId = ReadInt(row, "client_id"),
            KanaanId = Read(row, "kid"),
            LegacyMainClassId = mainClassId,
            MainClassName = mainClassId is not null && mainClassNamesById.TryGetValue(mainClassId.Value, out var mainClassName)
                ? mainClassName
                : Read(row, "main_class"),
            LegacySubClassId = subClassId,
            SubClassName = subClassId is not null && subClassNamesById.TryGetValue(subClassId.Value, out var subClassName)
                ? subClassName
                : Read(row, "sub_class"),
            SubClassExtra = Read(row, "sc_extra"),
            Administrator = Read(row, "administrator"),
            Product = Read(row, "product"),
            PolicyNumber = Read(row, "policy_no"),
            Description = Read(row, "description"),
            Fund = Read(row, "fund"),
            Value = ReadDecimal(row, "value"),
            LifeCover = isLifeAndDisabilityCover ? ReadDecimal(row, "value") : null,
            DisabilityCover = isLifeAndDisabilityCover ? ReadDecimal(row, "value02") : null,
            DreadDiseaseCover = isLifeAndDisabilityCover ? ReadDecimal(row, "value03") : null,
            CompulsoryContributionValue = isLifeAndDisabilityCover ? ReadDecimal(row, "value04") : null,
            VoluntaryContributionValue = isLifeAndDisabilityCover ? ReadDecimal(row, "value05") : null,
            Debt = ReadDecimal(row, "debt"),
            MonthlyPremium = ReadDecimal(row, "premium"),
            OnceOffPremium = ReadDecimal(row, "premium02"),
            MonthlyIncome = ReadDecimal(row, "income"),
            CapitalAdequacyRatioPercent = ReadDecimal(row, "car"),
            TaxPercent = ReadDecimal(row, "taxp"),
            IncludeInCalculations = ReadBool(row, "include") ?? false,
            SurrenderOrLiquidate = ReadBool(row, "surrender") ?? false,
            IsRetirementAnnuity = ReadBool(row, "ra") ?? false,
            IsPreservationFund = ReadBool(row, "pf") ?? false,
            IsRetrenchmentPackage = ReadBool(row, "rp") ?? false,
            IsQuote = ReadBool(row, "quote") ?? false,
            ValuationDate = ReadDateTime(row, "update_time"),
            OpenedBy = Read(row, "opened_by"),
            UpdatedBy = Read(row, "updated_by"),
            LegacyOpenedByUserId = ReadInt(row, "opened_by_id"),
            LegacyUpdatedByUserId = ReadInt(row, "updated_by_id"),
            LegacyOpenedAt = ReadDateTime(row, "date_opened"),
            LegacyUpdatedAt = ReadDateTime(row, "date_updated"),
            PayloadJson = JsonSerializer.Serialize(
                new SortedDictionary<string, string?>(row.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase),
                SnapshotJsonOptions),
            ImportedAtUtc = importedAtUtc
        };
    }

    public static void ApplyUpdatedValues(ClientKycPolicy target, ClientKycPolicy source)
    {
        target.LegacyClientId = source.LegacyClientId;
        target.KanaanId = source.KanaanId;
        target.LegacyMainClassId = source.LegacyMainClassId;
        target.MainClassName = source.MainClassName;
        target.LegacySubClassId = source.LegacySubClassId;
        target.SubClassName = source.SubClassName;
        target.SubClassExtra = source.SubClassExtra;
        target.Administrator = source.Administrator;
        target.Product = source.Product;
        target.PolicyNumber = source.PolicyNumber;
        target.Description = source.Description;
        target.Fund = source.Fund;
        target.Value = source.Value;
        target.LifeCover = source.LifeCover;
        target.DisabilityCover = source.DisabilityCover;
        target.DreadDiseaseCover = source.DreadDiseaseCover;
        target.CompulsoryContributionValue = source.CompulsoryContributionValue;
        target.VoluntaryContributionValue = source.VoluntaryContributionValue;
        target.Debt = source.Debt;
        target.MonthlyPremium = source.MonthlyPremium;
        target.OnceOffPremium = source.OnceOffPremium;
        target.MonthlyIncome = source.MonthlyIncome;
        target.CapitalAdequacyRatioPercent = source.CapitalAdequacyRatioPercent;
        target.TaxPercent = source.TaxPercent;
        target.IncludeInCalculations = source.IncludeInCalculations;
        target.SurrenderOrLiquidate = source.SurrenderOrLiquidate;
        target.IsRetirementAnnuity = source.IsRetirementAnnuity;
        target.IsPreservationFund = source.IsPreservationFund;
        target.IsRetrenchmentPackage = source.IsRetrenchmentPackage;
        target.IsQuote = source.IsQuote;
        target.ValuationDate = source.ValuationDate;
        target.OpenedBy = source.OpenedBy;
        target.UpdatedBy = source.UpdatedBy;
        target.LegacyOpenedByUserId = source.LegacyOpenedByUserId;
        target.LegacyUpdatedByUserId = source.LegacyUpdatedByUserId;
        target.LegacyOpenedAt = source.LegacyOpenedAt;
        target.LegacyUpdatedAt = source.LegacyUpdatedAt;
        target.PayloadJson = source.PayloadJson;
        target.ImportedAtUtc = source.ImportedAtUtc;
    }

    private static string? Read(IReadOnlyDictionary<string, string?> row, string name)
    {
        return row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string?> row, string name)
    {
        return Read(row, name)?.ToUpperInvariant() switch
        {
            "Y" or "YES" or "TRUE" or "1" => true,
            "N" or "NO" or "FALSE" or "0" => false,
            _ => null
        };
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        if (value is null)
        {
            return null;
        }

        var normalized = value.Replace("R", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }
}
