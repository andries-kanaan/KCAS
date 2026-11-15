using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class FundModelService(IDbContextFactory<ApplicationDbContext> factory)
{
    internal static async Task<string> Authorize(ApplicationDbContext db, ClaimsPrincipal principal, bool write)
    {
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal,
            write ? KcasPermissions.InvestmentsManage : KcasPermissions.InvestmentsView);
        return ComplianceWorkflowAccess.Label(actor);
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static void Audit(ApplicationDbContext db, int? fund, string action, string actor, object detail) =>
        db.ModelEvents.Add(new() { FundId = fund, Action = action, Actor = actor, AtUtc = DateTime.UtcNow, DetailJson = FundModelJson.Write(detail) });

    public async Task<List<ModelFund>> ListAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Authorize(db, principal, false);
        return await db.ModelFunds.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }
    public async Task<ModelFund> LoadAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Authorize(db, principal, false);
        return await db.ModelFunds.AsNoTracking().SingleAsync(x => x.Id == id);
    }
    public async Task<List<ModelSecurity>> SecuritiesAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Authorize(db, principal, false);
        return await db.ModelSecurities.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }
    public async Task<int> SaveAsync(ModelFund input, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || !System.Text.RegularExpressions.Regex.IsMatch(input.Currency, "^[A-Z]{3}$"))
            throw new ValidationException("A name and three-letter currency are required.");
        var holdings = FundModelJson.Read<List<ModelHolding>>(input.HoldingsJson);
        var scenarios = FundModelJson.Read<List<ModelScenario>>(input.ScenariosJson);
        if (holdings.Count > 150 || holdings.Select(x => x.Security).Distinct().Count() != holdings.Count || holdings.Any(x => !double.IsFinite(x.Value) || x.Value < 0 || x.Minimum < 0 || x.Maximum > 1 || x.Minimum > x.Maximum))
            throw new ValidationException("Holdings require distinct securities, nonnegative values and valid limits.");
        if (scenarios.Count > 30 || scenarios.Select(x => x.Name).Distinct().Count() != scenarios.Count)
            throw new ValidationException("Use distinct scenario names (maximum 30).");
        var row = input.Id == 0 ? new ModelFund() : await db.ModelFunds.SingleAsync(x => x.Id == input.Id);
        if (input.Id != 0 && row.Revision != input.Revision) throw new ValidationException("This fund changed. Reload before saving.");
        if (input.Id == 0) db.ModelFunds.Add(row);
        row.Name = input.Name.Trim(); row.Currency = input.Currency; row.Archived = input.Archived;
        if (!double.IsFinite(input.CashValue) || input.CashValue < 0) throw new ValidationException("Cash cannot be negative.");
        row.CashValue = input.CashValue;
        row.HoldingsJson = FundModelJson.Write(holdings); row.ScenariosJson = FundModelJson.Write(scenarios);
        row.Revision = Guid.NewGuid().ToString("N"); row.UpdatedUtc = DateTime.UtcNow;
        Audit(db, input.Id == 0 ? null : input.Id, "FundSaved", actor, new { row.Key, row.Name, row.Revision, holdings, scenarios });
        await db.SaveChangesAsync(); return row.Id;
    }
    public async Task SaveSecurityAsync(ModelSecurity input, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.Key, "^[A-Za-z0-9._:-]{1,100}$") || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || !System.Text.RegularExpressions.Regex.IsMatch(input.Currency, "^[A-Z]{3}$") || input.ProviderSymbol.Length > 50)
            throw new ValidationException("Use a stable security key, name and three-letter currency.");
        var row = await db.ModelSecurities.SingleOrDefaultAsync(x => x.Key == input.Key);
        if (row is null) { row = new ModelSecurity { Key = input.Key }; db.ModelSecurities.Add(row); }
        row.Name = input.Name; row.Currency = input.Currency; row.ProviderSymbol = input.ProviderSymbol;
        row.UpdatedUtc = DateTime.UtcNow;
        Audit(db, null, "SecuritySaved", actor, new { row.Key, row.Name, row.Currency, row.ProviderSymbol });
        await db.SaveChangesAsync();
    }
    public static List<ModelObservation> ParsePrices(string csv)
    {
        var lines = csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || !lines[0].Trim().Equals("Month,Price,TotalReturnIndex,Source", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Columns must be Month,Price,TotalReturnIndex,Source. Use YYYY-MM and decimal points.");
        var result = new List<ModelObservation>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',', 4);
            if (fields.Length != 4 || !DateTime.TryParseExact(fields[0]+"-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || !double.IsFinite(price) || price <= 0 || string.IsNullOrWhiteSpace(fields[3]))
                throw new ValidationException("Invalid price row: " + line[..Math.Min(120, line.Length)]);
            double? total = null;
            if (!string.IsNullOrWhiteSpace(fields[2]))
            {
                if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) || !double.IsFinite(t) || t <= 0) throw new ValidationException("Invalid total-return index.");
                total = t;
            }
            result.Add(new() { Month = fields[0], Price = price, TotalReturnIndex = total, Source = fields[3] });
        }
        if (result.Count > 2400 || result.Select(x => x.Month).Distinct().Count() != result.Count) throw new ValidationException("Duplicate months or too many observations.");
        return result.OrderBy(x => x.Month).ToList();
    }
    public async Task ImportPricesAsync(string key, List<ModelObservation> rows, bool replaceConflicts, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        var security = await db.ModelSecurities.SingleAsync(x => x.Key == key);
        var existing = FundModelJson.Read<List<ModelObservation>>(security.SeriesJson).ToDictionary(x => x.Month);
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.Month, out var old) && (old.Price != row.Price || old.TotalReturnIndex != row.TotalReturnIndex) && !replaceConflicts)
                throw new ValidationException($"{row.Month} differs from the saved price. Explicitly accept revisions to replace it.");
            existing[row.Month] = row;
        }
        Audit(db, null, "PricesImported", actor, new { key, previous = security.SeriesJson, imported = rows });
        security.SeriesJson = FundModelJson.Write(existing.Values.OrderBy(x => x.Month)); security.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
    public async Task<long> QueueAsync(int fundId, ModelScenario scenario, double cashValue, ClaimsPrincipal principal, bool force = false)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        var fund = await db.ModelFunds.SingleAsync(x => x.Id == fundId);
        if (fund.Archived) throw new ValidationException("Restore the archived fund before running models.");
        if (!double.IsFinite(cashValue) || cashValue < 0) throw new ValidationException("Cash cannot be negative.");
        var holdings = FundModelJson.Read<List<ModelHolding>>(fund.HoldingsJson);
        var keys = holdings.Select(x => x.Security).ToList();
        var securities = (await db.ModelSecurities.OrderBy(x => x.Key).ToListAsync()).Where(x => keys.Contains(x.Key)).ToList();
        if (securities.Count != keys.Count || keys.Count == 0) throw new ValidationException("Register all holdings and their price histories first.");
        var input = FundModelJson.Write(new { version = "markowitz-1", name = fund.Name, currency = fund.Currency,
            scenario, holdings, cashValue, securities = securities.Select(x => new { x.Key, x.Name, x.Currency, series = FundModelJson.Read<List<ModelObservation>>(x.SeriesJson) }) });
        var hash = Hash(input);
        if (!force)
        {
            var existing = await db.ModelRuns.Where(x => x.FundId == fundId && x.Fingerprint == hash && (x.State == "Complete" || x.State == "Queued" || x.State == "Running")).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            if (existing is not null) return existing.Id;
        }
        var run = new ModelRun { FundId = fundId, Fingerprint = hash, InputJson = input, Actor = actor, CreatedUtc = DateTime.UtcNow };
        db.ModelRuns.Add(run); Audit(db, fundId, "RunQueued", actor, new { run.Key, hash });
        await db.SaveChangesAsync(); return run.Id;
    }
    public async Task<List<ModelRun>> RunsAsync(int fundId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Authorize(db, principal, false);
        return await db.ModelRuns.AsNoTracking().Where(x => x.FundId == fundId && x.State != "Deleted").OrderByDescending(x => x.Id).Take(50).ToListAsync();
    }
    public async Task<ModelRun> RunAsync(long id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Authorize(db, principal, false);
        return await db.ModelRuns.AsNoTracking().SingleAsync(x => x.Id == id && x.State != "Deleted");
    }
    public async Task DeleteRunAsync(long id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var row = await db.ModelRuns.AsNoTracking().SingleAsync(x => x.Id == id);
        if (row.State == "Deleted") return;
        if (!CanDeleteRun(row.State)) throw new ValidationException("Cancel the active run and wait for it to stop before deleting it.");
        var changed = await db.ModelRuns.Where(x => x.Id == id && x.State == row.State)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, "Deleted"));
        if (changed != 1) throw new ValidationException("Run state changed. Refresh and try again.");
        Audit(db, row.FundId, "RunDeleted", actor, new { row.Id, row.Key, row.Fingerprint, previousState = row.State });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    public static bool CanDeleteRun(string state) => state is "Complete" or "Failed" or "Cancelled" or "Interrupted";
    public async Task DeleteScenarioAsync(int fundId, string name, string revision, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        var row = await db.ModelFunds.SingleAsync(x => x.Id == fundId);
        if (row.Revision != revision) throw new ValidationException("This fund changed. Reload before deleting a scenario.");
        var list = FundModelJson.Read<List<ModelScenario>>(row.ScenariosJson);
        var removed = list.SingleOrDefault(x => x.Name == name) ?? throw new ValidationException("Saved scenario no longer exists.");
        list.Remove(removed); row.ScenariosJson = FundModelJson.Write(list);
        row.Revision = Guid.NewGuid().ToString("N"); row.UpdatedUtc = DateTime.UtcNow;
        Audit(db, fundId, "ScenarioDeleted", actor, removed);
        await db.SaveChangesAsync();
    }
    public async Task CancelAsync(long id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Authorize(db, principal, true);
        var row = await db.ModelRuns.SingleAsync(x => x.Id == id);
        if (row.State is "Queued" or "Running") { row.CancelRequested = true; Audit(db, row.FundId, "RunCancellation", actor, new { id }); await db.SaveChangesAsync(); }
    }
}
