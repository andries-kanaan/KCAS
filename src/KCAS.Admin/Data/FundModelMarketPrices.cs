using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KCAS.Admin.Data;

// Public, best-effort history. No scheduler, account credentials or client data are sent.
public sealed class FundModelMarketPrices(IHttpClientFactory clients, IMemoryCache cache)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextRequest;
    public sealed record History(string Symbol, string Currency, DateTimeOffset RetrievedUtc, List<ModelObservation> Rows);

    public async Task<History> FetchAsync(string symbol, string currency, string firstMonth, CancellationToken ct)
    {
        if (!Regex.IsMatch(symbol, @"^[A-Za-z0-9^][A-Za-z0-9.^=\-]{0,49}$"))
            throw new ValidationException("Save the exact Yahoo Finance symbol for this share class first.");
        var start = DateTime.ParseExact(firstMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var key = $"FundPrices:{symbol}:{currency}:{firstMonth}:{DateTime.UtcNow:yyyy-MM}";
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<History>(key, out var cached) && cached is not null) return cached;
            if (cache.TryGetValue<string>(key + ":error", out var error)) throw new ValidationException(error);
            var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            nextRequest = DateTimeOffset.UtcNow.AddSeconds(2);
            var now = DateTimeOffset.UtcNow;
            var uri = "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) +
                $"?period1={new DateTimeOffset(start).ToUnixTimeSeconds()}&period2={now.ToUnixTimeSeconds()}&interval=1d&includeAdjustedClose=true";
            try
            {
                using var response = await clients.CreateClient("FundPublicPrices").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new ValidationException("The public price source is limiting requests. Try later or import a price file.");
                if (!response.IsSuccessStatusCode)
                    throw new ValidationException($"Public prices unavailable (HTTP {(int)response.StatusCode}). Check the symbol or import a price file.");
                await response.Content.LoadIntoBufferAsync(16 * 1024 * 1024, ct);
                var history = Parse(await response.Content.ReadAsStringAsync(ct), symbol, currency, now);
                cache.Set(key, history, TimeSpan.FromMinutes(15));
                return history;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or ValidationException)
            {
                ct.ThrowIfCancellationRequested();
                var reason = ex is ValidationException ? ex.Message : "Public price download failed or timed out. Saved prices are unchanged; try later or import a price file.";
                cache.Set(key + ":error", reason, TimeSpan.FromMinutes(2));
                throw new ValidationException(reason);
            }
        }
        finally { gate.Release(); }
    }

    public static History Parse(string json, string expectedSymbol, string expectedCurrency, DateTimeOffset retrieved)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1)
            throw new ValidationException("No public history found for this symbol. Use the exact exchange/share-class symbol or import a file.");
        var result = results[0];
        var meta = result.GetProperty("meta");
        var symbol = meta.GetProperty("symbol").GetString() ?? "";
        var currency = meta.GetProperty("currency").GetString() ?? "";
        if (!symbol.Equals(expectedSymbol, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Provider returned {symbol}, not {expectedSymbol}. No prices saved.");
        // GBp and GBP are deliberately not equivalent: quote units must not silently change.
        if (currency != expectedCurrency)
            throw new ValidationException($"Provider currency is {currency}; the security is registered as {expectedCurrency}. No conversion or prices saved.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(meta.GetProperty("exchangeTimezoneName").GetString()!); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ValidationException("Provider exchange timezone is not recognised; prices were not imported."); }
        var currentMonth = TimeZoneInfo.ConvertTime(retrieved, zone).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var stamps = result.GetProperty("timestamp");
        var indicators = result.GetProperty("indicators");
        var closes = indicators.GetProperty("quote")[0].GetProperty("close");
        JsonElement adjusted = default;
        if (indicators.TryGetProperty("adjclose", out var adjustment) && adjustment.GetArrayLength() > 0)
            adjusted = adjustment[0].GetProperty("adjclose");
        if (stamps.GetArrayLength() != closes.GetArrayLength() || (adjusted.ValueKind == JsonValueKind.Array && adjusted.GetArrayLength() != stamps.GetArrayLength()))
            throw new ValidationException("Provider timestamps and prices do not align.");
        var months = new SortedDictionary<string, (DateTimeOffset Date, double? Price, double? Total)>();
        for (var i = 0; i < stamps.GetArrayLength(); i++)
        {
            var date = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(stamps[i].GetInt64()), zone);
            var month = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(month, currentMonth) >= 0) continue;
            if (!months.TryGetValue(month, out var previous) || date > previous.Date)
                months[month] = (date, Positive(closes[i]), adjusted.ValueKind == JsonValueKind.Array ? Positive(adjusted[i]) : null);
        }
        var rows = new List<ModelObservation>();
        foreach (var (month, item) in months)
        {
            // A suspended/stale series or missing final quote is not a verified month-end close.
            if (item.Price is null || DateTime.DaysInMonth(item.Date.Year, item.Date.Month) - item.Date.Day > 7) continue;
            rows.Add(new() { Month = month, Price = item.Price.Value, TotalReturnIndex = item.Total,
                Source = $"Yahoo Finance {symbol}; {currency}; close {item.Date:yyyy-MM-dd}; retrieved {retrieved:O}; split-adjusted close / dividend-adjusted close; https://finance.yahoo.com/quote/{Uri.EscapeDataString(symbol)}/history/" });
        }
        if (rows.Count == 0) throw new ValidationException("No completed month-end prices available. Saved history is unchanged.");
        return new(symbol, currency, retrieved, rows);
    }

    private static double? Positive(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0 ? number : null;
}

public sealed record FundPriceChange(string Month, ModelObservation? Saved, ModelObservation Downloaded)
{
    public bool Changed => Saved is not null && (!Same(Saved.Price, Downloaded.Price) || !Same(Saved.TotalReturnIndex, Downloaded.TotalReturnIndex));
    public static bool Same(double? a, double? b) => a == b || a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) <= Math.Max(0.000001, Math.Abs(a.Value) * 0.0000001);
}

public sealed record FundPricePreview(string Key, string Name, string Symbol, string Currency, string Fingerprint,
    DateTimeOffset RetrievedUtc, List<FundPriceChange> Changes)
{
    public int Added => Changes.Count(x => x.Saved is null);
    public int Revised => Changes.Count(x => x.Changed);
}

public sealed class FundModelPriceRefreshService(IDbContextFactory<ApplicationDbContext> factory, FundModelMarketPrices market)
{
    private static string Fingerprint(ModelSecurity s) => FundModelService.Hash(FundModelJson.Write(new { s.Key, s.ProviderSymbol, s.Currency, s.SeriesJson }));

    public async Task<FundPricePreview> PreviewAsync(string key, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await FundModelService.Authorize(db, principal, true);
        var security = await db.ModelSecurities.AsNoTracking().SingleAsync(s => s.Key == key, ct);
        if (string.IsNullOrWhiteSpace(security.ProviderSymbol))
            throw new ValidationException("No online symbol saved. Select this security and save its exact Yahoo Finance symbol, or import a file.");
        var saved = FundModelJson.Read<List<ModelObservation>>(security.SeriesJson);
        var start = saved.Count > 0 ? saved.Min(x => x.Month)! : DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var downloaded = await market.FetchAsync(security.ProviderSymbol, security.Currency, start, ct);
        return new(security.Key, security.Name, downloaded.Symbol, downloaded.Currency, Fingerprint(security), downloaded.RetrievedUtc,
            downloaded.Rows.Select(p => new FundPriceChange(p.Month, saved.SingleOrDefault(x => x.Month == p.Month), p)).ToList());
    }

    public async Task ApplyAsync(FundPricePreview preview, bool acceptRevisions, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await FundModelService.Authorize(db, principal, true);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var security = await db.ModelSecurities.AsNoTracking().SingleAsync(s => s.Key == preview.Key);
        if (Fingerprint(security) != preview.Fingerprint) throw new ValidationException("Security or prices changed since this preview. Refresh again before saving.");
        if (preview.Revised > 0 && !acceptRevisions) throw new ValidationException("Review and explicitly accept the historical price revisions before saving.");
        var rows = FundModelJson.Read<List<ModelObservation>>(security.SeriesJson).ToDictionary(p => p.Month);
        // Do not splice an adjusted series into an incompletely replaced old scale.
        if (preview.Revised > 0 && rows.Keys.Except(preview.Changes.Select(p => p.Month)).Any())
            throw new ValidationException("The public source does not cover every saved month. Revised prices cannot safely be mixed with that incomplete history; reconcile using a price file.");
        var changed = preview.Changes.Where(c => c.Saved is null || c.Changed).ToList();
        if (changed.Count == 0) return;
        foreach (var item in changed) rows[item.Month] = item.Downloaded;
        var json = FundModelJson.Write(rows.Values.OrderBy(p => p.Month));
        var count = await db.ModelSecurities.Where(s => s.Id == security.Id && s.SeriesJson == security.SeriesJson && s.ProviderSymbol == security.ProviderSymbol && s.Currency == security.Currency)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.SeriesJson, json).SetProperty(s => s.UpdatedUtc, DateTime.UtcNow));
        if (count != 1) throw new ValidationException("Security changed during refresh. Reload before saving.");
        FundModelService.Audit(db, null, "PublicPricesRefreshed", actor,
            new { preview.Key, preview.Symbol, preview.Currency, preview.RetrievedUtc, preview.Added, preview.Revised, previous = security.SeriesJson, changed });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
