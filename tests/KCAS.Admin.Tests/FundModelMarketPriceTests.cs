using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using KCAS.Admin.Data;
using Microsoft.Extensions.Caching.Memory;

namespace KCAS.Admin.Tests;

public class FundModelMarketPriceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-15T12:00:00Z");
    private static string Payload(string symbol = "TEST", string currency = "USD", string zone = "Europe/London", double? last = 110) => JsonSerializer.Serialize(new {
        chart = new { result = new[] { new {
            meta = new { symbol, currency, exchangeTimezoneName = zone },
            timestamp = new[] { Stamp("2026-09-29T23:30:00Z"), Stamp("2026-09-30T23:30:00Z"), Stamp("2026-10-30T16:00:00Z"), Stamp("2026-11-13T16:00:00Z") },
            indicators = new { quote = new[] { new { close = new double?[] {100, 101, last, 120} } }, adjclose = new[] { new { adjclose = new double?[] {98, 99, 108, 120} } } }
        } }, error = (string?)null }
    });
    private static long Stamp(string date) => DateTimeOffset.Parse(date).ToUnixTimeSeconds();

    [Fact]
    public void CompletedMonthsUseExchangeDateLastQuoteAndSeparateReturnBasis()
    {
        var history = FundModelMarketPrices.Parse(Payload(), "TEST", "USD", Now);
        Assert.Equal(2, history.Rows.Count);
        Assert.Equal("2026-09", history.Rows[0].Month);
        Assert.Equal(100, history.Rows[0].Price); // The next UTC September quote is October in London.
        Assert.Equal(98, history.Rows[0].TotalReturnIndex);
        Assert.Equal(110, history.Rows[1].Price);
        Assert.Contains("close 2026-10-30", history.Rows[1].Source);
        Assert.DoesNotContain(history.Rows, r => r.Month == "2026-11");
    }
    [Fact]
    public void MissingFinalQuoteDoesNotSubstituteEarlierPrice()
    {
        var history = FundModelMarketPrices.Parse(Payload(last:null), "TEST", "USD", Now);
        Assert.Single(history.Rows);
        Assert.Equal("2026-09", history.Rows[0].Month);
    }
    [Theory]
    [InlineData("OTHER", "USD", "Europe/London")]
    [InlineData("TEST", "GBp", "Europe/London")]
    [InlineData("TEST", "USD", "Not/AZone")]
    public void WrongSecurityCurrencyOrTimezoneRejected(string symbol, string currency, string zone) =>
        Assert.Throws<ValidationException>(() => FundModelMarketPrices.Parse(Payload(symbol,currency,zone), "TEST", "USD", Now));

    [Fact]
    public void ProviderErrorsAreNotEmptySuccess() => Assert.Throws<ValidationException>(() => FundModelMarketPrices.Parse("{\"chart\":{\"result\":null,\"error\":{\"code\":\"Not Found\"}}}", "TEST", "USD", Now));

    [Fact]
    public void RevisionDetectionIgnoresFloatingNoiseButNotMissingAdjustedPrices()
    {
        Assert.False(new FundPriceChange("2026-10", new(){Price=100}, new(){Price=100.00000001}).Changed);
        Assert.True(new FundPriceChange("2026-10", new(){Price=100}, new(){Price=100,TotalReturnIndex=99}).Changed);
        Assert.True(new FundPriceChange("2026-10", new(){Price=100}, new(){Price=101}).Changed);
    }
    [Fact]
    public async Task RepeatedManualDownloadsUseCache()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, Payload());
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var market = new FundModelMarketPrices(new ClientFactory(client), cache);
        var first = await market.FetchAsync("TEST", "USD", "2026-09", default);
        var second = await market.FetchAsync("TEST", "USD", "2026-09", default);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Count);
        Assert.Equal("query1.finance.yahoo.com", handler.LastUri!.Host);
        Assert.Contains("interval=1d", handler.LastUri.Query);
    }
    [Fact]
    public async Task RateLimitingDoesNotRetryOrReturnPrices()
    {
        using var handler = new StubHandler(HttpStatusCode.TooManyRequests, "rate limited");
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var market = new FundModelMarketPrices(new ClientFactory(client), cache);
        for(var i=0;i<2;i++) await Assert.ThrowsAsync<ValidationException>(() => market.FetchAsync("TEST", "USD", "2026-09", default));
        Assert.Equal(1, handler.Count);
    }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Count++; LastUri=request.RequestUri; return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(json)}); }
    }
}
