using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class InvestmentReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2023, 1, 1);
    private static readonly DateOnly End = Start.AddDays(730);

    [Fact]
    public void Annualised_return_matches_compounded_growth_and_ignores_balance_snapshots()
    {
        var account = Account();
        account.Transactions.Add(new() { Id = 99, TransactionDate = Start.AddDays(200), BalanceZar = 110, IsFinal = true });
        var report = Build(account, 121);
        AssertRate(10, report.ShortTerm);
        AssertRate(10, report.LongTerm);
        Assert.Equal(21m, report.ShortTerm.Gain);
        Assert.Equal(2, report.ShortTerm.CashFlows.Count);
    }

    [Fact]
    public void Topup_is_weighted_by_its_actual_date()
    {
        var account = Account();
        account.Transactions.Add(Tx(2, Start.AddDays(365), contribution: 50));
        AssertRate(10, Build(account, 176).ShortTerm);
    }

    [Fact]
    public void Withdrawal_is_a_payment_to_investor_not_a_loss()
    {
        var account = Account();
        account.Transactions.Add(Tx(2, Start.AddDays(365), withdrawal: 20));
        AssertRate(10, Build(account, 99).ShortTerm);
    }

    [Fact]
    public void Losses_are_reported_with_negative_returns()
    {
        AssertRate(-10, Build(Account(), 81).ShortTerm);
    }

    [Fact]
    public void Zero_ending_value_is_total_loss_not_missing_value()
    {
        AssertRate(-100, Build(Account(), 0).ShortTerm);
    }

    [Fact]
    public void Transfer_is_internal_long_term_but_funding_for_current_segment()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var review = Transfer(original, next);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [review]);
        AssertRate(20, report.ShortTerm);
        AssertRate((Math.Sqrt(1.32) - 1) * 100, report.LongTerm);
        Assert.Equal(Start, report.LongTerm.StartDate);
        Assert.Equal(2, report.History.Count);
        Assert.Equal(2, report.LongTerm.CashFlows.Count(x => x.IsInternalTransfer));
        Assert.Equal(32m, report.LongTerm.Gain);
    }

    [Fact]
    public void Older_unknown_predecessor_is_not_hidden_by_a_more_recent_verified_transfer()
    {
        var original = Account();
        original.LegacyLinkedAccountId = 999;
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("earlier linked"));
    }

    [Fact]
    public void Transfer_to_different_owner_does_not_merge_client_returns()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 110);
        next.ClientId = 2;
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("another client"));
    }

    [Fact]
    public void Multiple_predecessors_need_allocation_instead_of_arbitrary_chain_selection()
    {
        var original = Account();
        var other = Account(3);
        original.SurrenderDate = Start.AddDays(365);
        other.SurrenderDate = original.SurrenderDate;
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        other.Transactions.Add(Tx(3, other.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, Start.AddDays(365), 110);
        var report = InvestmentReturnCalculator.Build(next, [original, other, next], [Value(next, 132)],
            [Transfer(original, next), Transfer(other, next)]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("Multiple predecessor"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Staged_terminal_payments_match_split_or_aggregated_inception_funding(bool aggregate)
    {
        var original = Account();
        original.Transactions.Add(Tx(2, Start.AddDays(100), withdrawal: 5));
        original.SurrenderDate = Start.AddDays(375);
        original.Transactions.Add(Tx(3, Start.AddDays(350), withdrawal: 80));
        original.Transactions.Add(Tx(4, Start.AddDays(360), withdrawal: 30));
        var next = Account(2, original.SurrenderDate.Value, aggregate ? 110 : 80);
        if (!aggregate) next.Transactions.Add(Tx(5, next.InvestmentDate!.Value, contribution: 30));
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.True(report.LongTerm.IsAvailable, string.Join("; ", report.LongTerm.Issues));
        Assert.Equal(Start, report.LongTerm.StartDate);
        Assert.Equal(aggregate ? 3 : 4, report.LongTerm.CashFlows.Count(x => x.IsInternalTransfer));
        Assert.Contains(report.LongTerm.CashFlows, x => x.TransactionId == 2 && !x.IsInternalTransfer);
        Assert.Equal(37, report.LongTerm.Gain);
    }

    [Fact]
    public void Later_topup_source_is_not_a_competing_original_predecessor()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var laterSource = Account(3);
        laterSource.SurrenderDate = Start.AddDays(500);
        laterSource.Transactions.Add(Tx(3, laterSource.SurrenderDate.Value, withdrawal: 50));
        // The unrelated source's older instruction does not form part of this return's history.
        var recurring = Tx(4, Start.AddDays(100), contribution: 1);
        recurring.InvestmentFrequency = "Monthly";
        laterSource.Transactions.Add(recurring);
        next.Transactions.Add(Tx(5, laterSource.SurrenderDate.Value, contribution: 50));
        var value = Value(next, 132);
        var reviews = new[] { Transfer(original, next), Transfer(laterSource, next) };
        var report = InvestmentReturnCalculator.Build(next, [original, laterSource, next], [value], reviews);
        Assert.True(report.LongTerm.IsAvailable, string.Join("; ", report.LongTerm.Issues));
        Assert.Equal(new[] { original.Id, next.Id }, report.History.Select(x => x.Id));
        Assert.Equal(Start, report.LongTerm.StartDate);
        Assert.Single(report.LongTerm.CashFlows, x => x.TransactionId == 5 && !x.IsInternalTransfer && x.Amount == -50);
        Assert.DoesNotContain(report.LongTerm.CashFlows, x => x.AccountId == laterSource.Id);
        Assert.Contains(report.LongTerm.Notes, x => x.Contains(laterSource.AccountNumber!));
        Assert.Equal(-18, report.LongTerm.Gain);
    }

    [Fact]
    public void Original_cash_funding_with_only_later_source_links_retains_original_inception()
    {
        var account = Account();
        var source = Account(2);
        source.SurrenderDate = Start.AddDays(365);
        source.Transactions.Add(Tx(2, source.SurrenderDate.Value, withdrawal: 50));
        account.Transactions.Add(Tx(3, source.SurrenderDate.Value, contribution: 50));
        var report = InvestmentReturnCalculator.Build(account, [account, source], [Value(account, 176)], [Transfer(source, account)]);
        AssertRate(10, report.LongTerm);
        Assert.Single(report.History);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("missing contribution")]
    [InlineData("different owner")]
    public void Unsupported_later_source_link_is_reported(string gap)
    {
        var account = Account();
        var source = Account(2);
        source.SurrenderDate = Start.AddDays(365);
        source.Transactions.Add(Tx(2, source.SurrenderDate.Value, withdrawal: 50));
        var review = Transfer(source, account);
        if (gap != "missing contribution") account.Transactions.Add(Tx(3, source.SurrenderDate.Value, contribution: 50));
        if (gap == "stale") source.Transactions.Add(Tx(4, Start.AddDays(100), contribution: 1));
        if (gap == "different owner") source.ClientId = 99;
        var report = InvestmentReturnCalculator.Build(account, [account, source], [Value(account, 176)], [review]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
    }

    [Fact]
    public void Closure_after_reinvestment_does_not_misclassify_original_funding_as_topup()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(375);
        original.Transactions.Add(Tx(2, Start.AddDays(360), withdrawal: 110));
        var next = Account(2, Start.AddDays(365), 110);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.True(report.LongTerm.IsAvailable, string.Join("; ", report.LongTerm.Issues));
        Assert.Equal(Start, report.LongTerm.StartDate);
        Assert.Equal(2, report.History.Count);
    }

    [Fact]
    public void Transfer_does_not_select_arbitrary_older_withdrawals_to_make_amounts_match()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, Start.AddDays(100), withdrawal: 110));
        original.Transactions.Add(Tx(3, original.SurrenderDate.Value, withdrawal: 120));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("matched paid-out"));
    }

    [Fact]
    public void Circular_link_is_not_reclassified_as_later_funding()
    {
        var account = Account();
        account.SurrenderDate = End;
        account.Transactions.Add(Tx(2, End, withdrawal: 121));
        var report = InvestmentReturnCalculator.Build(account, [account], [], [Transfer(account, account)]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("circular"));
    }

    [Fact]
    public void Contributions_between_payout_stages_need_allocation()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, Start.AddDays(350), withdrawal: 80));
        original.Transactions.Add(Tx(3, Start.AddDays(355), contribution: 10));
        original.Transactions.Add(Tx(4, Start.AddDays(360), withdrawal: 30));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("matched paid-out"));
    }

    [Fact]
    public void Same_day_terminal_payments_are_not_arbitrarily_split()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        original.Transactions.Add(Tx(3, original.SurrenderDate.Value, withdrawal: 5));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [Transfer(original, next)]);
        Assert.False(report.LongTerm.IsAvailable);
    }

    [Fact]
    public void Three_segment_chain_excludes_both_transfers_and_preserves_external_topups()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var middle = Account(2, original.SurrenderDate.Value, 110);
        middle.SurrenderDate = End;
        middle.Transactions.Add(Tx(3, End, withdrawal: 121));
        var next = Account(3, End, 121);
        var value = Value(next, 133.1m);
        value.ValuationDate = End.AddDays(365);
        var report = InvestmentReturnCalculator.Build(next, [original, middle, next], [value],
            [Transfer(original, middle), Transfer(middle, next)]);
        AssertRate(10, report.ShortTerm);
        AssertRate(10, report.LongTerm);
        Assert.Equal(4, report.LongTerm.CashFlows.Count(x => x.IsInternalTransfer));
        Assert.Equal(33.1m, report.LongTerm.Gain);
    }

    [Fact]
    public void Unmatched_transfer_amounts_do_not_silently_become_external_cash_flows()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 100);
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 120)], [Transfer(original, next)]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.LongTerm.Issues, x => x.Contains("matched paid-out"));
    }

    [Fact]
    public void Stale_transfer_review_blocks_only_long_term()
    {
        var original = Account();
        original.SurrenderDate = Start.AddDays(365);
        original.Transactions.Add(Tx(2, original.SurrenderDate.Value, withdrawal: 110));
        var next = Account(2, original.SurrenderDate.Value, 110);
        var review = Transfer(original, next);
        original.Transactions.Add(Tx(3, Start.AddDays(100), contribution: 10));
        var report = InvestmentReturnCalculator.Build(next, [original, next], [Value(next, 132)], [review]);
        Assert.True(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
    }

    [Theory]
    [InlineData("Monthly")]
    [InlineData("Annually")]
    [InlineData(null)]
    public void Recurring_or_unknown_instructions_are_not_assumed_to_be_actual_cash_flows(string? frequency)
    {
        var account = Account();
        var tx = Tx(2, Start.AddDays(365), withdrawal: 20);
        tx.InvestmentFrequency = frequency;
        account.Transactions.Add(tx);
        var report = Build(account, 99);
        Assert.False(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.ShortTerm.Issues, x => x.Contains("payment dates"));
    }

    [Fact]
    public void Deleted_movements_and_movements_after_valuation_are_excluded()
    {
        var account = Account();
        var deleted = Tx(2, Start.AddDays(365), withdrawal: 20);
        deleted.IsDeleted = true;
        account.Transactions.Add(deleted);
        account.Transactions.Add(Tx(3, End.AddDays(1), contribution: 999));
        AssertRate(10, Build(account, 121).ShortTerm);
    }

    [Fact]
    public void Missing_original_amount_does_not_use_a_later_balance_as_capital()
    {
        var account = Account();
        account.Transactions.Clear();
        account.Transactions.Add(new() { TransactionDate = Start, BalanceZar = 100, IsFinal = true });
        Assert.False(Build(account, 121).ShortTerm.IsAvailable);
    }

    [Fact]
    public void Mixed_valuation_dates_cannot_be_added_as_one_closing_value()
    {
        var account = Account();
        var first = Value(account, 60);
        var second = Value(account, 61);
        second.ValuationDate = End.AddDays(-31);
        var report = InvestmentReturnCalculator.Build(account, [account], [first, second], []);
        Assert.False(report.ShortTerm.IsAvailable);
    }

    [Fact]
    public void Missing_valuation_date_reports_a_gap_instead_of_throwing()
    {
        var account = Account();
        var valuation = Value(account, 121);
        valuation.ValuationDate = null;
        Assert.False(InvestmentReturnCalculator.Build(account, [account], [valuation], []).ShortTerm.IsAvailable);
    }

    [Fact]
    public void Multiple_fund_lines_on_same_date_are_aggregated_once()
    {
        var account = Account();
        var report = InvestmentReturnCalculator.Build(account, [account], [Value(account, 60), Value(account, 61)], []);
        AssertRate(10, report.ShortTerm);
    }

    [Fact]
    public void Same_account_number_under_another_client_is_not_double_counted()
    {
        var account = Account();
        var other = Account(2);
        other.ClientId = 2;
        other.AccountNumber = account.AccountNumber;
        var report = InvestmentReturnCalculator.Build(account, [account, other], [Value(account, 121), Value(other, 999)], []);
        AssertRate(10, report.ShortTerm);
    }

    [Fact]
    public void Duplicate_valuation_account_does_not_receive_a_return()
    {
        var account = Account();
        var duplicate = Account(2);
        duplicate.AccountNumber = account.AccountNumber;
        var report = InvestmentReturnCalculator.Build(account, [account, duplicate], [Value(account, 121)], []);
        Assert.False(report.ShortTerm.IsAvailable);
    }

    [Fact]
    public void Missing_surrender_payout_does_not_assume_all_capital_was_lost()
    {
        var account = Account();
        account.SurrenderDate = End;
        var report = InvestmentReturnCalculator.Build(account, [account], [], []);
        Assert.False(report.ShortTerm.IsAvailable);
        Assert.Contains(report.ShortTerm.Issues, x => x.Contains("final payout"));
    }

    [Fact]
    public void Historical_surrender_uses_actual_payout_not_old_balance()
    {
        var account = Account();
        account.SurrenderDate = End;
        account.Transactions.Add(Tx(2, End, withdrawal: 121));
        AssertRate(10, InvestmentReturnCalculator.Build(account, [account], [], []).ShortTerm);
    }

    [Fact]
    public void Reviewed_staged_payouts_before_administrative_closure_use_actual_dates()
    {
        var account = Account();
        account.SurrenderDate = End.AddDays(10);
        account.Transactions.Add(Tx(2, Start.AddDays(365), withdrawal: 55));
        account.Transactions.Add(Tx(3, End, withdrawal: 60.5m));
        var review = ClosedReview(account);
        var report = InvestmentReturnCalculator.Build(account, [account], [], [review]);
        AssertRate(10, report.ShortTerm);
        AssertRate(10, report.LongTerm);
        Assert.Equal(End, report.ShortTerm.EndDate);
        Assert.Equal(End, report.LongTerm.EndDate);
        Assert.Equal(3, report.ShortTerm.CashFlows.Count);
        Assert.DoesNotContain(report.ShortTerm.CashFlows, x => x.Date == account.SurrenderDate);
    }

    [Fact]
    public void Reviewed_transfer_closure_accepts_earlier_full_payment_for_account_return()
    {
        var account = Account();
        account.SurrenderDate = End.AddDays(10);
        account.Transactions.Add(Tx(2, End, withdrawal: 121));
        var review = ClosedReview(account);
        review.Outcome = ClientInvestmentReconciliationOutcomes.Transferred;
        AssertRate(10, InvestmentReturnCalculator.Build(account, [account], [], [review]).ShortTerm);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("no evidence")]
    [InlineData("follow-up")]
    [InlineData("wrong date")]
    [InlineData("superseded")]
    public void Earlier_partial_withdrawals_are_not_assumed_to_close_the_account(string gap)
    {
        var account = Account();
        account.SurrenderDate = End;
        account.Transactions.Add(Tx(2, Start.AddDays(365), withdrawal: 20));
        var review = ClosedReview(account);
        var reviews = new List<ClientInvestmentReconciliationReview> { review };
        switch (gap)
        {
            case "missing": reviews.Clear(); break;
            case "stale": account.Transactions.Add(Tx(3, Start.AddDays(100), contribution: 10)); break;
            case "no evidence": review.EvidenceReference = " "; break;
            case "follow-up": review.Outcome = ClientInvestmentReconciliationOutcomes.NeedsFollowUp; break;
            case "wrong date": review.AppliedSurrenderDate = End.AddDays(-1); break;
            case "superseded": reviews.Add(new() { Id = 99, ClientInvestmentAccountId = account.Id,
                Outcome = ClientInvestmentReconciliationOutcomes.NeedsFollowUp, ReviewedAtUtc = review.ReviewedAtUtc.AddDays(1) }); break;
        }
        var report = InvestmentReturnCalculator.Build(account, [account], [], reviews);
        Assert.False(report.ShortTerm.IsAvailable);
        Assert.False(report.LongTerm.IsAvailable);
        Assert.Contains(report.ShortTerm.Issues, x => x.Contains("complete closure"));
    }

    [Fact]
    public void Movement_after_surrender_is_reported_not_silently_omitted()
    {
        var account = Account();
        account.SurrenderDate = End;
        account.Transactions.Add(Tx(2, End, withdrawal: 121));
        account.Transactions.Add(Tx(3, End.AddDays(10), withdrawal: 10));
        var report = InvestmentReturnCalculator.Build(account, [account], [], [ClosedReview(account)]);
        Assert.False(report.ShortTerm.IsAvailable);
        Assert.Contains(report.ShortTerm.Issues, x => x.Contains("after surrender"));
    }

    private static ClientInvestmentReconciliationReview ClosedReview(ClientInvestmentAccount account) => new()
    {
        ClientInvestmentAccountId = account.Id, Outcome = ClientInvestmentReconciliationOutcomes.HistoricalSurrendered,
        AppliedSurrenderDate = account.SurrenderDate, EvidenceReference = "Full redemption and settlement evidence",
        SnapshotSha256 = InvestmentReconciliationService.CalculateSnapshot(account, [])
    };

    [Fact]
    public void Missing_historical_currency_amount_blocks_currency_return()
    {
        var account = Account();
        var valuation = Value(account, 121);
        valuation.AmountForeign = 121;
        var report = InvestmentReturnCalculator.Build(account, [account], [valuation], [], "USD");
        Assert.False(report.ShortTerm.IsAvailable);
        Assert.Contains(report.ShortTerm.Issues, x => x.Contains("historical exchange"));
    }

    [Fact]
    public void Multiple_roots_are_not_presented_as_one_precise_rate()
    {
        var result = new InvestmentReturnResult();
        result.CashFlows.Add(new(Start, -100, 1, 1, "Capital"));
        result.CashFlows.Add(new(Start.AddDays(365), 230, 1, 2, "Withdrawal"));
        result.CashFlows.Add(new(End, -132, 1, 3, "Top-up"));
        InvestmentReturnCalculator.Calculate(result);
        Assert.False(result.IsAvailable);
        Assert.Contains(result.Issues, x => x.Contains("multiple possible"));
    }

    [Fact]
    public void Under_one_year_result_is_annualised_using_actual_days()
    {
        var account = Account();
        var valuation = Value(account, 105);
        valuation.ValuationDate = Start.AddDays(182);
        AssertRate((Math.Pow(1.05, 365.0 / 182) - 1) * 100,
            InvestmentReturnCalculator.Build(account, [account], [valuation], []).ShortTerm);
    }

    private static ClientInvestmentAccount Account(int id = 1, DateOnly? start = null, decimal amount = 100)
    {
        var account = new ClientInvestmentAccount { Id = id, ClientId = 1, AccountNumber = $"A{id}",
            Administrator = "Test administrator", InvestmentDate = start ?? Start };
        account.Transactions.Add(Tx(id * 10, start ?? Start, contribution: amount));
        return account;
    }
    private static ClientInvestmentTransaction Tx(int id, DateOnly date, decimal? contribution = null, decimal? withdrawal = null) =>
        new() { Id = id, TransactionDate = date, InvestmentAmountZar = contribution,
            WithdrawalAmountZar = withdrawal, IsFinal = true, InvestmentFrequency = "Once Off" };
    private static ClientFundValuation Value(ClientInvestmentAccount account, decimal value) => new()
    {
        ClientId = account.ClientId, InvestmentUniqueNumber = account.AccountNumber, Administrator = account.Administrator,
        AmountZar = value, ValuationDate = End
    };
    private static ClientInvestmentReconciliationReview Transfer(ClientInvestmentAccount original, ClientInvestmentAccount next) => new()
    {
        ClientInvestmentAccountId = original.Id, RelatedClientInvestmentAccountId = next.Id,
        Outcome = ClientInvestmentReconciliationOutcomes.Transferred, AppliedSurrenderDate = original.SurrenderDate,
        EvidenceReference = "Test transfer statement", SnapshotSha256 = InvestmentReconciliationService.CalculateSnapshot(original, [])
    };
    private static InvestmentReturnReport Build(ClientInvestmentAccount account, decimal value) =>
        InvestmentReturnCalculator.Build(account, [account], [Value(account, value)], []);
    private static void AssertRate(double expected, InvestmentReturnResult result)
    {
        Assert.True(result.IsAvailable, string.Join("; ", result.Issues));
        Assert.InRange((double)result.AnnualisedPercent!.Value, expected - 0.0001, expected + 0.0001);
    }
}
