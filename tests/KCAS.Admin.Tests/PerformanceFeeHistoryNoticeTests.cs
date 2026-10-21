using KCAS.Admin.Components.Shared;
using KCAS.Admin.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeHistoryNoticeTests
{
    [Fact]
    public void Existing_opening_names_the_exact_missing_history_before_any_preparation()
    {
        var document = PerformanceFeeStatementReader.Read(PerformanceFeeHistoryTests.SummaryWorkbook(), "later.xlsx");
        var ledger = FeeStatementLedger.Merge([document], "SYNTHETIC-IA");
        var notice = FeeEarlierHistoryNotice.From("SYNTHETIC-IA", ledger);
        Assert.NotNull(notice);
        Assert.Equal("SYNTHETIC-IA", notice.Account);
        Assert.Equal(new DateOnly(2021, 1, 1), notice.StatementFrom);
        Assert.Equal(100000, notice.OpeningValue);
        Assert.Equal(new DateOnly(2020, 12, 31), notice.RequiredThrough);
    }

    [Fact]
    public void Adding_the_earlier_statement_clears_the_notice_immediately_on_combined_preview()
    {
        var later = PerformanceFeeStatementReader.Read(PerformanceFeeHistoryTests.SummaryWorkbook(), "later.xlsx");
        var opening = later.Rows.Single(x => x.Kind == "Opening");
        var start = new DateOnly(2020, 1, 1);
        FeeStatementRow Row(string kind, DateOnly date, decimal value, decimal units, decimal balance) => new()
        {
            Account = opening.Account, Wrap = opening.Wrap, Holding = opening.Holding, Kind = kind,
            Date = date, Amount = value, Units = units, UnitBalance = balance, Price = opening.Price,
            Key = "earlier-" + kind, Source = "earlier.xlsx " + kind
        };
        var earlier = new FeeStatementDocument
        {
            FileName = "earlier.xlsx", Sha256 = "synthetic-earlier",
            Periods = [new() { Account = opening.Account, From = start, To = opening.Date, OpeningValue = 0,
                ClosingValue = opening.Amount, Entries = [new("Lump sum investment", opening.Amount, "earlier.xlsx funding")] }],
            Rows = [Row("Opening", start, 0, 0, 0),
                Row("Contribution", start.AddMonths(6), opening.Amount, opening.UnitBalance, opening.UnitBalance),
                Row("Closing", opening.Date, opening.Amount, 0, opening.UnitBalance)]
        };
        Assert.NotNull(FeeEarlierHistoryNotice.From(opening.Account, FeeStatementLedger.Merge([later], opening.Account)));
        var combined = FeeStatementLedger.Merge([later, earlier], opening.Account);
        Assert.True(combined.History.BeginningCovered);
        Assert.Null(FeeEarlierHistoryNotice.From(opening.Account, combined));
    }

    [Fact]
    public void Complete_inception_and_non_chargeable_scopes_do_not_raise_the_earlier_history_notice()
    {
        var document = PerformanceFeeStatementReader.Read(PerformanceFeeHistoryTests.SummaryWorkbook(zero: true), "complete.xlsx");
        Assert.Null(FeeEarlierHistoryNotice.From("SYNTHETIC-IA", FeeStatementLedger.Merge([document], "SYNTHETIC-IA")));
        var ledger = Incomplete(); ledger.Wraps = [new() { Code = "KP1" }];
        Assert.Null(FeeEarlierHistoryNotice.From("SYNTHETIC", ledger));
        Assert.Null(FeeEarlierHistoryNotice.From("SYNTHETIC", new()));
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    public void Only_a_verified_checkpoint_covering_every_chargeable_wrap_replaces_earlier_history(
        bool allocated, bool dated, bool referenced, bool noticeExpected)
    {
        var ledger = Incomplete(); ledger.Wraps.Add(new() { Code = "KE1" });
        var date = dated ? new DateOnly(2024, 6, 1) : new DateOnly(2023, 12, 31);
        FeeCheckpoint Point() => new(date, 100, 120, 110, referenced ? "Actual fee checkpoint" : "");
        var checkpoint = new FeeCheckpoint(date, 200, 240, 220, "Verified client checkpoint")
        { Wraps = allocated ? [new("KA6", Point()), new("KE1", Point())] : [new("KA6", Point())] };
        var profile = new PerformanceFeeProfile { CheckpointJson = FeeJson.Write(checkpoint) };
        Assert.Equal(noticeExpected, FeeEarlierHistoryNotice.From("SYNTHETIC", ledger, profile) is not null);
    }

    [Fact]
    public async Task Visible_notice_groups_accounts_and_provides_an_upload_action_without_expanding_details()
    {
        var html = await Render([
            new("SYNTHETIC-A", new(2024, 1, 1), 250000, new(2023, 12, 31)),
            new("SYNTHETIC-B", new(2023, 6, 1), 100000, new(2023, 5, 31))], "#client-statements");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Earlier AIMS / Glacier statement required", html);
        Assert.Contains("SYNTHETIC-A", html); Assert.Contains("SYNTHETIC-B", html);
        Assert.Contains("2023-12-31", html); Assert.Contains("2023-05-31", html);
        Assert.Contains("receiving transfers", html);
        Assert.Contains("href=\"#client-statements\"", html);
        Assert.DoesNotContain("<details", html);
    }

    [Fact]
    public async Task No_notice_renders_when_covered_and_read_only_view_has_no_upload_action()
    {
        Assert.Equal("", (await Render([], "#client-statements")).Trim());
        var html = await Render([new("SYNTHETIC", new(2024, 1, 1), 100, new(2023, 12, 31))], null);
        Assert.Contains("role=\"alert\"", html);
        Assert.DoesNotContain("href=", html);
    }

    private static FeeStatementLedger Incomplete() => new()
    {
        History = new() { Periods = [new() { Account = "SYNTHETIC", From = new(2024, 1, 1), To = new(2026, 1, 1), OpeningValue = 100 }] },
        Wraps = [new() { Code = "KA6" }]
    };

    private static async Task<string> Render(FeeEarlierHistoryNotice[] notices, string? target)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<FeeEarlierHistoryAlert>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(FeeEarlierHistoryAlert.Notices)] = notices, [nameof(FeeEarlierHistoryAlert.UploadTarget)] = target }));
            return output.ToHtmlString();
        });
    }
}
