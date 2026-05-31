using System.Globalization;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyFundValuationImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static ClientFundValuation Map(IReadOnlyDictionary<string, string?> row, int clientId, DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy fund row is missing a numeric id.");

        return new ClientFundValuation
        {
            ClientId = clientId,
            LegacyFundId = legacyId,
            LegacyClientId = ReadInt(row, "client_id"),
            KanaanId = Read(row, "kid"),
            FundName = Read(row, "name") ?? string.Empty,
            AmountForeign = ReadDecimal(row, "amount"),
            AmountZar = ReadDecimal(row, "r_amount"),
            FundDescription = Read(row, "fund_description"),
            CompanyClientNumber = Read(row, "company_client_number"),
            Administrator = Read(row, "company_name"),
            ProductName = Read(row, "company_product"),
            ProductType = Read(row, "company_product_type"),
            CompanyDescription = Read(row, "company_description"),
            InvestmentUniqueNumber = Read(row, "investment_unique_number"),
            OpenedBy = Read(row, "opened_by"),
            UpdatedBy = Read(row, "updated_by"),
            LegacyOpenedByUserId = ReadInt(row, "opened_by_id"),
            LegacyUpdatedByUserId = ReadInt(row, "updated_by_id"),
            LegacyOpenedAt = ReadDateTime(row, "date_opened"),
            LegacyUpdatedAt = ReadDateTime(row, "date_updated"),
            ValuationDate = ReadDateOnly(row, "update_time"),
            PayloadJson = SerializePayload(row),
            ImportedAtUtc = importedAtUtc
        };
    }

    public static void ApplyUpdatedValues(ClientFundValuation target, ClientFundValuation source)
    {
        target.LegacyClientId = source.LegacyClientId;
        target.KanaanId = source.KanaanId;
        target.FundName = source.FundName;
        target.AmountForeign = source.AmountForeign;
        target.AmountZar = source.AmountZar;
        target.FundDescription = source.FundDescription;
        target.CompanyClientNumber = source.CompanyClientNumber;
        target.Administrator = source.Administrator;
        target.ProductName = source.ProductName;
        target.ProductType = source.ProductType;
        target.CompanyDescription = source.CompanyDescription;
        target.InvestmentUniqueNumber = source.InvestmentUniqueNumber;
        target.OpenedBy = source.OpenedBy;
        target.UpdatedBy = source.UpdatedBy;
        target.LegacyOpenedByUserId = source.LegacyOpenedByUserId;
        target.LegacyUpdatedByUserId = source.LegacyUpdatedByUserId;
        target.LegacyOpenedAt = source.LegacyOpenedAt;
        target.LegacyUpdatedAt = source.LegacyUpdatedAt;
        target.ValuationDate = source.ValuationDate;
        target.PayloadJson = source.PayloadJson;
        target.ImportedAtUtc = source.ImportedAtUtc;
    }

    private static string SerializePayload(IReadOnlyDictionary<string, string?> row) =>
        JsonSerializer.Serialize(
            new SortedDictionary<string, string?>(row.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase),
            SnapshotJsonOptions);

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

    private static DateOnly? ReadDateOnly(IReadOnlyDictionary<string, string?> row, string name)
    {
        var value = Read(row, name);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDateTime)
            ? DateOnly.FromDateTime(parsedDateTime)
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
