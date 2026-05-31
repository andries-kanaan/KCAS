using System.Text.Json;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyInvestmentImportMapperTests
{
    [Fact]
    public void Investment_account_mapper_preserves_legacy_fields_audit_and_snapshot()
    {
        var account = LegacyInvestmentAccountImportMapper.Map(AccountRow(), clientId: 10, new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(25, account.LegacyInvestmentAccountId);
        Assert.Equal(10, account.ClientId);
        Assert.Equal(99, account.LegacyClientId);
        Assert.Equal(new DateOnly(2020, 1, 15), account.InvestmentDate);
        Assert.Equal(new DateOnly(2025, 2, 28), account.SurrenderDate);
        Assert.Equal("Glacier", account.Administrator);
        Assert.Equal(3, account.LegacyAdministratorId);
        Assert.Equal("ACC-123", account.AccountNumber);
        Assert.Equal("Retirement Annuity", account.ProductName);
        Assert.Equal("Compulsory", account.ProductType);
        Assert.Equal("Stable SA", account.FundName);
        Assert.True(account.IsLinkedHead);
        Assert.Equal(20, account.LegacyLinkedAccountId);
        Assert.True(account.IsFinal);

        using var document = JsonDocument.Parse(account.PayloadJson);
        Assert.Equal("Glacier", document.RootElement.GetProperty("lisp").GetString());
    }

    [Fact]
    public void Investment_transaction_mapper_preserves_amounts_flags_audit_and_snapshot()
    {
        var transaction = LegacyInvestmentTransactionImportMapper.Map(TransactionRow(), investmentAccountId: 12, new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(77, transaction.LegacyInvestmentHistoryId);
        Assert.Equal(12, transaction.ClientInvestmentAccountId);
        Assert.Equal(25, transaction.LegacyInvestmentAccountId);
        Assert.Equal(new DateOnly(2024, 3, 1), transaction.TransactionDate);
        Assert.Equal("Monthly contribution", transaction.Description);
        Assert.Equal(18.75m, transaction.ExchangeRate);
        Assert.Equal(100m, transaction.InvestmentAmountForeign);
        Assert.Equal(1875m, transaction.InvestmentAmountZar);
        Assert.Equal(20m, transaction.WithdrawalAmountForeign);
        Assert.Equal(375m, transaction.WithdrawalAmountZar);
        Assert.Equal("Monthly", transaction.InvestmentFrequency);
        Assert.Equal(7.5m, transaction.AnnualIncreasePercent);
        Assert.Equal(5000m, transaction.BalanceForeign);
        Assert.Equal(93750m, transaction.BalanceZar);
        Assert.False(transaction.IsDeleted);
        Assert.True(transaction.IsFinal);

        using var document = JsonDocument.Parse(transaction.PayloadJson);
        Assert.Equal("Monthly contribution", document.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void Investment_transaction_mapper_treats_invalid_numbers_as_null_but_keeps_payload()
    {
        var row = TransactionRow();
        row["r_bal"] = "not a number";

        var transaction = LegacyInvestmentTransactionImportMapper.Map(row, investmentAccountId: 12, DateTime.UtcNow);

        Assert.Null(transaction.BalanceZar);

        using var document = JsonDocument.Parse(transaction.PayloadJson);
        Assert.Equal("not a number", document.RootElement.GetProperty("r_bal").GetString());
    }

    [Fact]
    public void ApplyUpdatedValues_replaces_imported_investment_fields()
    {
        var targetAccount = LegacyInvestmentAccountImportMapper.Map(AccountRow(), clientId: 10, DateTime.UtcNow);
        var accountRow = AccountRow();
        accountRow["lisp"] = "Updated administrator";
        accountRow["final"] = "0";
        var sourceAccount = LegacyInvestmentAccountImportMapper.Map(accountRow, clientId: 10, DateTime.UtcNow);

        LegacyInvestmentAccountImportMapper.ApplyUpdatedValues(targetAccount, sourceAccount);

        Assert.Equal("Updated administrator", targetAccount.Administrator);
        Assert.False(targetAccount.IsFinal);

        var targetTransaction = LegacyInvestmentTransactionImportMapper.Map(TransactionRow(), investmentAccountId: 12, DateTime.UtcNow);
        var transactionRow = TransactionRow();
        transactionRow["description"] = "Updated transaction";
        transactionRow["del"] = "Y";
        var sourceTransaction = LegacyInvestmentTransactionImportMapper.Map(transactionRow, investmentAccountId: 12, DateTime.UtcNow);

        LegacyInvestmentTransactionImportMapper.ApplyUpdatedValues(targetTransaction, sourceTransaction);

        Assert.Equal("Updated transaction", targetTransaction.Description);
        Assert.True(targetTransaction.IsDeleted);
    }

    private static Dictionary<string, string?> AccountRow()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "25",
            ["investment_date"] = "2020-01-15",
            ["surrender_date"] = "2025-02-28",
            ["lisp"] = "Glacier",
            ["lisp_id"] = "3",
            ["lisp_investment_no"] = "ACC-123",
            ["lisp_product"] = "Retirement Annuity",
            ["lisp_product_id"] = "8",
            ["product_type"] = "Compulsory",
            ["product_type_id"] = "0",
            ["fund"] = "Stable SA",
            ["fund_id"] = "4",
            ["ialinkhead"] = "1",
            ["ialink_id"] = "20",
            ["client_id"] = "99",
            ["final"] = "1",
            ["opened_by"] = "legacy user",
            ["updated_by"] = "legacy updater",
            ["opened_by_id"] = "7",
            ["updated_by_id"] = "8",
            ["date_opened"] = "2020-01-15 09:00:00",
            ["date_updated"] = "2026-05-31 10:00:00"
        };
    }

    private static Dictionary<string, string?> TransactionRow()
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "77",
            ["investment_date"] = "2024-03-01",
            ["description"] = "Monthly contribution",
            ["ia_id"] = "25",
            ["xr"] = "18,75",
            ["investment_amount"] = "100",
            ["r_investment_amount"] = "1875",
            ["withdrawal_amount"] = "20",
            ["r_withdrawal_amount"] = "375",
            ["investment_frequency"] = "Monthly",
            ["annual_p_increase"] = "7,5%",
            ["bal"] = "5000",
            ["r_bal"] = "93750",
            ["del"] = "N",
            ["final"] = "1",
            ["opened_by"] = "legacy user",
            ["updated_by"] = "legacy updater",
            ["opened_by_id"] = "7",
            ["updated_by_id"] = "8",
            ["date_opened"] = "2024-03-01 09:00:00",
            ["date_updated"] = "2026-05-31 10:00:00"
        };
    }
}
