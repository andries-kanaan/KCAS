using System.Text.Json;

namespace KCAS.Admin.Data;

public sealed record FeeEarlierHistoryNotice(string Account, DateOnly StatementFrom, decimal OpeningValue, DateOnly RequiredThrough)
{
    public static FeeEarlierHistoryNotice? From(string account, FeeStatementLedger ledger, PerformanceFeeProfile? profile = null)
    {
        var history = ledger.History;
        var first = history.Periods.MinBy(x => x.From);
        var chargeableWraps = ledger.Wraps.Where(x => x.Code is "KE1" or "KA6" or "KA9").Select(x => x.Code).ToArray();
        if (history.BeginningCovered || history.FeeScopeBeginningCovered || first is null || first.OpeningValue == 0 || first.From == DateOnly.MinValue || chargeableWraps.Length == 0)
            return null;

        // A verified checkpoint is an alternative to reconstructing earlier fee history.
        if (profile?.CheckpointJson is { } json && JsonSerializer.Deserialize<FeeCheckpoint>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) is { } checkpoint &&
            checkpoint.Date >= first.From && chargeableWraps.All(code => checkpoint.Wraps.Any(w => w.Code == code &&
                w.Checkpoint.Date == checkpoint.Date && w.Checkpoint.Value >= 0 && w.Checkpoint.Benchmark >= 0 &&
                w.Checkpoint.HighWaterMark >= 0 && !string.IsNullOrWhiteSpace(w.Checkpoint.Reference))))
            return null;

        return new(account, first.From, first.OpeningValue, first.From.AddDays(-1));
    }
}
