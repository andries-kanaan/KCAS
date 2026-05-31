using System.Globalization;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyInvestmentAccountImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static ClientInvestmentAccount Map(IReadOnlyDictionary<string, string?> row, int clientId, DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy investment account row is missing a numeric id.");

        return new ClientInvestmentAccount
        {
            ClientId = clientId,
            LegacyInvestmentAccountId = legacyId,
            LegacyClientId = ReadInt(row, "client_id"),
            InvestmentDate = ReadDateOnly(row, "investment_date"),
            SurrenderDate = ReadDateOnly(row, "surrender_date"),
            Administrator = Read(row, "lisp"),
            LegacyAdministratorId = ReadInt(row, "lisp_id"),
            AccountNumber = Read(row, "lisp_investment_no"),
            ProductName = Read(row, "lisp_product"),
            LegacyProductId = ReadInt(row, "lisp_product_id"),
            ProductType = Read(row, "product_type"),
            LegacyProductTypeId = ReadInt(row, "product_type_id"),
            FundName = Read(row, "fund"),
            LegacyFundId = ReadInt(row, "fund_id"),
            IsLinkedHead = ReadBool(row, "ialinkhead") ?? false,
            LegacyLinkedAccountId = ReadInt(row, "ialink_id"),
            IsFinal = ReadBool(row, "final") ?? false,
            OpenedBy = Read(row, "opened_by"),
            UpdatedBy = Read(row, "updated_by"),
            LegacyOpenedByUserId = ReadInt(row, "opened_by_id"),
            LegacyUpdatedByUserId = ReadInt(row, "updated_by_id"),
            LegacyOpenedAt = ReadDateTime(row, "date_opened"),
            LegacyUpdatedAt = ReadDateTime(row, "date_updated"),
            PayloadJson = SerializePayload(row),
            ImportedAtUtc = importedAtUtc
        };
    }

    public static void ApplyUpdatedValues(ClientInvestmentAccount target, ClientInvestmentAccount source)
    {
        target.LegacyClientId = source.LegacyClientId;
        target.InvestmentDate = source.InvestmentDate;
        target.SurrenderDate = source.SurrenderDate;
        target.Administrator = source.Administrator;
        target.LegacyAdministratorId = source.LegacyAdministratorId;
        target.AccountNumber = source.AccountNumber;
        target.ProductName = source.ProductName;
        target.LegacyProductId = source.LegacyProductId;
        target.ProductType = source.ProductType;
        target.LegacyProductTypeId = source.LegacyProductTypeId;
        target.FundName = source.FundName;
        target.LegacyFundId = source.LegacyFundId;
        target.IsLinkedHead = source.IsLinkedHead;
        target.LegacyLinkedAccountId = source.LegacyLinkedAccountId;
        target.IsFinal = source.IsFinal;
        target.OpenedBy = source.OpenedBy;
        target.UpdatedBy = source.UpdatedBy;
        target.LegacyOpenedByUserId = source.LegacyOpenedByUserId;
        target.LegacyUpdatedByUserId = source.LegacyUpdatedByUserId;
        target.LegacyOpenedAt = source.LegacyOpenedAt;
        target.LegacyUpdatedAt = source.LegacyUpdatedAt;
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

    private static bool? ReadBool(IReadOnlyDictionary<string, string?> row, string name)
    {
        return Read(row, name)?.ToUpperInvariant() switch
        {
            "Y" or "YES" or "TRUE" or "1" => true,
            "N" or "NO" or "FALSE" or "0" => false,
            _ => null
        };
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
