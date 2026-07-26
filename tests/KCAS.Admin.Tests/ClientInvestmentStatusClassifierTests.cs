using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class ClientInvestmentStatusClassifierTests
{
    private static readonly DateOnly AsAt = new(2026, 7, 26);

    [Fact]
    public void Matched_valuation_without_surrender_is_current()
    {
        var account = Account("LA-123");
        var status = ClientInvestmentStatusClassifier.Evaluate(
            account,
            [Valuation("LA123", 100m)],
            AsAt);

        Assert.True(status.IsCurrent);
        Assert.False(status.NeedsStatusCorrection);
    }

    [Fact]
    public void Account_without_current_value_is_historical_and_requires_status_correction()
    {
        var status = ClientInvestmentStatusClassifier.Evaluate(
            Account("OLD-1"),
            [],
            AsAt);

        Assert.False(status.IsCurrent);
        Assert.True(status.NeedsStatusCorrection);
        Assert.Contains("No current valuation", status.Reason);
    }

    [Fact]
    public void Surrendered_account_is_historical_without_status_correction()
    {
        var account = Account("OLD-2");
        account.SurrenderDate = new DateOnly(2020, 12, 18);

        var status = ClientInvestmentStatusClassifier.Evaluate(
            account,
            [],
            AsAt);

        Assert.False(status.IsCurrent);
        Assert.False(status.NeedsStatusCorrection);
        Assert.Contains("Surrendered 2020-12-18", status.Reason);
    }

    private static ClientInvestmentAccount Account(string number) => new()
    {
        AccountNumber = number,
        Administrator = "AIMS"
    };

    private static ClientFundValuation Valuation(string number, decimal amount) => new()
    {
        InvestmentUniqueNumber = number,
        Administrator = "AIMS, ABSA",
        AmountZar = amount
    };
}
