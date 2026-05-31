using System.Globalization;
using System.Text.Json;
using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyInvestmentTransactionImportMapper
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    public static ClientInvestmentTransaction Map(IReadOnlyDictionary<string, string?> row, int investmentAccountId, DateTime importedAtUtc)
    {
        var legacyId = ReadInt(row, "id") ?? throw new InvalidOperationException("Legacy investment history row is missing a numeric id.");

        return new ClientInvestmentTransaction
        {
            ClientInvestmentAccountId = investmentAccountId,
            LegacyInvestmentHistoryId = legacyId,
            LegacyInvestmentAccountId = ReadInt(row, "ia_id"),
            TransactionDate = ReadDateOnly(row, "investment_date"),
            Description = Read(row, "description"),
            ExchangeRate = ReadDecimal(row, "xr"),
            InvestmentAmountForeign = ReadDecimal(row, "investment_amount"),
            InvestmentAmountZar = ReadDecimal(row, "r_investment_amount"),
            WithdrawalAmountForeign = ReadDecimal(row, "withdrawal_amount"),
            WithdrawalAmountZar = ReadDecimal(row, "r_withdrawal_amount"),
            InvestmentFrequency = Read(row, "investment_frequency"),
            AnnualIncreasePercent = ReadDecimal(row, "annual_p_increase"),
            BalanceForeign = ReadDecimal(row, "bal"),
            BalanceZar = ReadDecimal(row, "r_bal"),
            IsDeleted = ReadBool(row, "del") ?? false,
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

    public static void ApplyUpdatedValues(ClientInvestmentTransaction target, ClientInvestmentTransaction source)
    {
        target.LegacyInvestmentAccountId = source.LegacyInvestmentAccountId;
        target.TransactionDate = source.TransactionDate;
        target.Description = source.Description;
        target.ExchangeRate = source.ExchangeRate;
        target.InvestmentAmountForeign = source.InvestmentAmountForeign;
        target.InvestmentAmountZar = source.InvestmentAmountZar;
        target.WithdrawalAmountForeign = source.WithdrawalAmountForeign;
        target.WithdrawalAmountZar = source.WithdrawalAmountZar;
        target.InvestmentFrequency = source.InvestmentFrequency;
        target.AnnualIncreasePercent = source.AnnualIncreasePercent;
        target.BalanceForeign = source.BalanceForeign;
        target.BalanceZar = source.BalanceZar;
        target.IsDeleted = source.IsDeleted;
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
