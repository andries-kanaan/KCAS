using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed record AumSnapshotInfo(long Id, string Source, DateOnly AsOf, int Holdings, double Opening, AumFund[] Funds, AumRunAssumptions Assumptions);
public sealed record AumSavedRunInfo(long Id, DateTime At, string Actor, string Name);
public sealed record AumSavedProjection(int EngineVersion, long SnapshotId, string SourceHash, DateOnly AsOf, string Name,
    string Source, AumFund[] DefaultRates, Dictionary<string, double> Overrides, AumProjectionResult Defaults, AumProjectionResult Active)
{
    public AumRunAssumptions? Assumptions { get; init; }
}

public sealed record AumRunAssumptions(double MonthlyFeeIncome, double FeeBaseAum, double BrokerShare, double BrokerCost, double PdfIncomeShare,
    double OffshorePdfEnabled, double SaHurdle, double SaEffectiveRate, double UsCpi2025, double FutureUsCpi,
    double SupportThreshold, double SupportRate, double RetirementRate, int RecoveryMonth, double RecoveryShare, double RecoveryCar,
    AumBusinessBaseline? BusinessBaseline, AumMortalityBasis? MortalityBasis, AumFeeTerms[] FeeTerms)
{
    public static AumRunAssumptions From(AumSnapshot s) => new(s.MonthlyFeeIncome, s.FeeBaseAum, s.BrokerShare, s.BrokerWrapperCost,
        s.PdfIncomeShare, s.OffshorePdfEnabled, s.SaHurdle, s.SaPdfRate, s.UsCpi2025, s.FutureUsCpi, s.SupportThreshold, s.SupportRate,
        s.RetirementRate, s.RecoveryMonth, s.RecoveryShare, s.RecoveryCar, s.BusinessBaseline, s.MortalityBasis, s.FeeTerms);
}

public sealed class AumProjectionService(IDbContextFactory<ApplicationDbContext> factory)
{
    public const int MaxSnapshotBytes = 12 * 1024 * 1024;
    private const string SnapshotEvent = "AumProjectionSnapshot";
    private const string RunEvent = "AumProjectionRun";
    private const string RunNameEvent = "AumProjectionRunName";

    public async Task<AumSnapshotInfo?> LatestAsync(ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        await FundModelTransferService.AuthorizeAsync(db, user);
        var row = await db.ModelEvents.AsNoTracking().Where(e => e.Action == SnapshotEvent).OrderByDescending(e => e.Id).FirstOrDefaultAsync();
        return row is null ? null : Info(row.Id, Parse(row.DetailJson));
    }

    public async Task<AumSnapshotInfo> ImportAsync(string json, ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await FundModelTransferService.AuthorizeAsync(db, user);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxSnapshotBytes) throw new ValidationException("Snapshot exceeds 12 MB.");
        var snapshot = Parse(json);
        AumProjectionEngine.Run(snapshot);
        var entry = new ModelEvent { Action = SnapshotEvent, Actor = actor, AtUtc = DateTime.UtcNow, DetailJson = FundModelJson.Write(snapshot) };
        db.ModelEvents.Add(entry);
        await db.SaveChangesAsync();
        return Info(entry.Id, snapshot);
    }

    public async Task<AumSavedProjection> PreviewAsync(long snapshotId, string name, Dictionary<string, double> overrides, ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        await FundModelTransferService.AuthorizeAsync(db, user);
        return await CalculateAsync(db, snapshotId, name, overrides);
    }

    public async Task<long> SaveAsync(long snapshotId, string name, Dictionary<string, double> overrides, ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await FundModelTransferService.AuthorizeAsync(db, user);
        var run = await CalculateAsync(db, snapshotId, name, overrides);
        await using var tx = await db.Database.BeginTransactionAsync();
        var entry = new ModelEvent { Action = RunEvent, Actor = actor, AtUtc = DateTime.UtcNow, DetailJson = FundModelJson.Write(run) };
        db.ModelEvents.Add(entry);
        await db.SaveChangesAsync();
        FundModelService.Audit(db, null, RunNameEvent, actor, new { id = entry.Id, run.Name });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entry.Id;
    }

    public async Task<AumSavedRunInfo[]> RecentAsync(ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        await FundModelTransferService.AuthorizeAsync(db, user);
        var entries = await db.ModelEvents.AsNoTracking().Where(e => e.Action == RunNameEvent)
            .OrderByDescending(e => e.Id).Take(30).ToListAsync();
        return entries.Select(e =>
        {
            using var json = JsonDocument.Parse(e.DetailJson);
            return new AumSavedRunInfo(json.RootElement.GetProperty("id").GetInt64(), e.AtUtc, e.Actor, json.RootElement.GetProperty("name").GetString()!);
        }).ToArray();
    }

    public async Task<AumSavedProjection?> LoadAsync(long id, ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        await FundModelTransferService.AuthorizeAsync(db, user);
        var entry = await db.ModelEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.Action == RunEvent);
        return entry is null ? null : FundModelJson.Read<AumSavedProjection>(entry.DetailJson);
    }

    private static async Task<AumSavedProjection> CalculateAsync(ApplicationDbContext db, long id, string name, Dictionary<string, double> overrides)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ValidationException("Enter a scenario name (maximum 120 characters).");
        var entry = await db.ModelEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.Action == SnapshotEvent)
            ?? throw new ValidationException("The input snapshot is not available.");
        var s = Parse(entry.DetailJson);
        var baseline = AumProjectionEngine.Run(s);
        return new(2, id, s.SourceHash, s.AsOf, name.Trim(), s.Source, s.Funds, new(overrides),
            baseline, overrides.Count == 0 ? baseline : AumProjectionEngine.Run(s, overrides))
            { Assumptions = AumRunAssumptions.From(s) };
    }

    private static AumSnapshotInfo Info(long id, AumSnapshot s)
        => new(id, s.Source, s.AsOf, s.Holdings.Length, s.Holdings.Sum(h => h.Opening), s.Funds, AumRunAssumptions.From(s));

    private static AumSnapshot Parse(string json)
    {
        AumSnapshot s;
        try { s = FundModelJson.Read<AumSnapshot>(json) ?? throw new ValidationException("Empty snapshot."); }
        catch (JsonException) { throw new ValidationException("This is not a supported AUM projection snapshot."); }
        AumProjectionEngine.Validate(s);
        return s;
    }
}
