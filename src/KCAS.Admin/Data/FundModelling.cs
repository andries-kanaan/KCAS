using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ModelFund
{
    public int Id { get; set; }
    [MaxLength(64)] public string Key { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(3)] public string Currency { get; set; } = "USD";
    public bool Archived { get; set; }
    public double CashValue { get; set; }
    public string HoldingsJson { get; set; } = "[]";
    public string ScenariosJson { get; set; } = "[]";
    [MaxLength(64), ConcurrencyCheck] public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ModelSecurity
{
    public int Id { get; set; }
    [MaxLength(100)] public string Key { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(3)] public string Currency { get; set; } = "USD";
    [MaxLength(50)] public string ProviderSymbol { get; set; } = "";
    public string SeriesJson { get; set; } = "[]";
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ModelRun
{
    public long Id { get; set; }
    [MaxLength(64)] public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public int FundId { get; set; }
    [MaxLength(64)] public string Fingerprint { get; set; } = "";
    [MaxLength(30)] public string State { get; set; } = "Queued";
    [MaxLength(400)] public string Progress { get; set; } = "Queued";
    public string InputJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    [MaxLength(256)] public string Actor { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public bool CancelRequested { get; set; }
}

public sealed class ModelEvent
{
    public long Id { get; set; }
    public int? FundId { get; set; }
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(256)] public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public string DetailJson { get; set; } = "{}";
}

public sealed class ModelHolding
{
    public string Security { get; set; } = "";
    public double Value { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; } = .15;
    public string? From { get; set; }
    public string? To { get; set; }
    public bool Included { get; set; } = true;
}
public sealed class ModelObservation
{
    public string Month { get; set; } = "";
    public double Price { get; set; }
    public double? TotalReturnIndex { get; set; }
    public string Source { get; set; } = "";
}
public sealed class ModelScenario
{
    public string Name { get; set; } = "Conservative 13%";
    public double Risk { get; set; } = .13;
    public double Cap { get; set; } = .15;
    public double CashMin { get; set; }
    public double CashMax { get; set; } = 1;
    public double CashAnnual { get; set; }
    public double CovShrink { get; set; } = .25;
    public double MeanShrink { get; set; } = .5;
    public double Cost { get; set; } = .001;
    public int Window { get; set; } = 60;
    public int MinimumHistory { get; set; } = 36;
    public int RebalanceMonths { get; set; } = 12;
    public string Start { get; set; } = "2022-03";
    public string End { get; set; } = "2026-10";
    public string Basis { get; set; } = "Price";
    public string Universe { get; set; } = "Selected";
    public string Mode { get; set; } = "Backtest";
}

public static class FundModelJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    public static void Configure(ModelBuilder b)
    {
        b.Entity<ModelFund>().HasIndex(x => x.Key).IsUnique();
        b.Entity<ModelSecurity>().HasIndex(x => x.Key).IsUnique();
        b.Entity<ModelRun>().HasIndex(x => x.Key).IsUnique();
        b.Entity<ModelRun>().HasIndex(x => new { x.FundId, x.Fingerprint });
        b.Entity<ModelRun>().HasOne<ModelFund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
    }
}
