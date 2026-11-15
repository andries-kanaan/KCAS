using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class FundModelPackage
{
    public int Version { get; set; } = 1;
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceSystem { get; set; } = "";
    public string ExportedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ModelFund> Funds { get; set; } = [];
    public List<ModelSecurity> Securities { get; set; } = [];
    public List<FundModelPackageRun> Runs { get; set; } = [];
}
public sealed record FundModelPackageRun(string FundKey, int SourceFundId, long SourceRunId, ModelRun Run);
public sealed record FundModelTransferExport(string PackageId, string FileName, int Funds, int Securities, int Runs);
public sealed record FundModelTransferRow(string Kind, string Key, string Name, string Action);
public sealed class FundModelTransferPreview
{
    public FundModelPackage Package { get; init; } = new();
    public List<FundModelTransferRow> Rows { get; } = [];
    public List<string> Conflicts { get; } = [];
    public string Stamp { get; set; } = "";
    public bool CanApply => Conflicts.Count == 0;
}
internal sealed record FundModelTransferBaseline(string SourceSystem, string Kind, string Key, string Digest);

public sealed class FundModelTransferService(IDbContextFactory<ApplicationDbContext> factory, IConfiguration configuration, IHostEnvironment environment)
{
    private string StorageRoot => Path.GetFullPath(configuration["FundModelling:TransferStorageRoot"] ??
        (Directory.Exists(@"D:\Deploy\KCAS\shared") ? @"D:\Deploy\KCAS\shared\fund-model-packages" : Path.Combine(environment.ContentRootPath,"..","..","backups","fund-model-packages")));
    private static string Hash<T>(T value) => FundModelService.Hash(FundModelJson.Write(value));
    internal static string FundDigest(ModelFund f) => Hash(new { f.Key, f.Name, f.Currency, f.Archived, f.CashValue, f.HoldingsJson, f.ScenariosJson });
    internal static string SecurityDigest(ModelSecurity s) => Hash(new { s.Key, s.Name, s.Currency, s.ProviderSymbol, s.SeriesJson });
    internal static string RunDigest(string fundKey, ModelRun r) => Hash(new { fundKey, r.Key, r.Fingerprint, r.InputJson, r.ResultJson, r.Actor, r.CreatedUtc, r.FinishedUtc });
    private static void Require(bool condition, string error) { if (!condition) throw new ValidationException(error); }
    // Explicit equalities avoid this MySQL provider's unsupported collection-parameter translation.
    private static Expression<Func<T,bool>> MatchingKeys<T>(IEnumerable<string> keys)
    {
        var row=Expression.Parameter(typeof(T),"row");Expression matches=Expression.Constant(false);
        foreach(var key in keys)matches=Expression.OrElse(matches,Expression.Equal(Expression.Property(row,"Key"),Expression.Constant(key)));
        return Expression.Lambda<Func<T,bool>>(matches,row);
    }

    internal static async Task<string> AuthorizeAsync(ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var id=principal.FindFirstValue(ClaimTypes.NameIdentifier)??"";
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id);
        var admin=await (from link in db.UserRoles where link.UserId==id join role in db.Roles on link.RoleId equals role.Id where role.Name==KcasRoles.Administrator select role.Id).AnyAsync();
        if(principal.Identity?.IsAuthenticated!=true || user?.IsApproved!=true || !admin) throw new UnauthorizedAccessException("Current approved Administrator access is required for fund modelling transfers.");
        return user.Email??user.UserName??user.Id;
    }

    public async Task<List<ModelFund>> FundsAsync(ClaimsPrincipal user)
    {
        await using var db=await factory.CreateDbContextAsync();await AuthorizeAsync(db,user);
        return await db.ModelFunds.AsNoTracking().OrderBy(f=>f.Name).ToListAsync();
    }
    public async Task<FundModelTransferExport> ExportAsync(IEnumerable<int> fundIds, bool includeRuns, string passphrase, string reason, ClaimsPrincipal user)
    {
        FundModelTransferEncryption.ValidatePassphrase(passphrase);Require(!string.IsNullOrWhiteSpace(reason)&&reason.Length<=2000,"Enter an export reason (maximum 2,000 characters).");
        var ids=fundIds.Distinct().ToList();Require(ids.Count is >0 and <=25,"Select 1 to 25 funds.");
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor=await AuthorizeAsync(db,user);
        var funds=(await db.ModelFunds.AsNoTracking().ToListAsync()).Where(f=>ids.Contains(f.Id)).OrderBy(f=>f.Key).ToList();
        Require(funds.Count==ids.Count,"A selected fund no longer exists.");
        var keys=funds.SelectMany(f=>FundModelJson.Read<List<ModelHolding>>(f.HoldingsJson)).Select(h=>h.Security).ToHashSet(StringComparer.Ordinal);
        var package=new FundModelPackage {ExportedBy=actor,Reason=reason.Trim(),SourceSystem=Hash(new { machine=Environment.MachineName, source=db.Database.GetDbConnection().DataSource, database=db.Database.GetDbConnection().Database }),Funds=funds,
            Securities=(await db.ModelSecurities.AsNoTracking().OrderBy(s=>s.Key).ToListAsync()).Where(s=>keys.Contains(s.Key)).ToList()};
        if(includeRuns)
            foreach(var f in funds)
                package.Runs.AddRange((await db.ModelRuns.AsNoTracking().Where(r=>r.FundId==f.Id&&r.State=="Complete").OrderBy(r=>r.Id).ToListAsync()).Select(r=>new FundModelPackageRun(f.Key,f.Id,r.Id,r)));
        Validate(package);
        var encrypted=FundModelTransferEncryption.Encrypt(JsonSerializer.SerializeToUtf8Bytes(package,FundModelJson.Options),passphrase);
        var name=$"KCAS-fund-models-{DateTime.UtcNow:yyyyMMdd}-{package.PackageId}.kcas-fund-models";
        Directory.CreateDirectory(StorageRoot);await File.WriteAllBytesAsync(Path.Combine(StorageRoot,name),encrypted);
        FundModelService.Audit(db,null,"FundModelPackageExported",actor,new {package.PackageId,name,package.Reason,funds=package.Funds.Select(f=>new{f.Key,f.Name}),runs=package.Runs.Count});
        await db.SaveChangesAsync();await tx.CommitAsync();
        return new(package.PackageId,name,package.Funds.Count,package.Securities.Count,package.Runs.Count);
    }
    public async Task<(Stream Content,string FileName)?> DownloadAsync(string id,ClaimsPrincipal user)
    {
        await using var db=await factory.CreateDbContextAsync();await AuthorizeAsync(db,user);
        if(!Guid.TryParseExact(id,"N",out _))return null;
        var audit=await db.ModelEvents.AsNoTracking().Where(e=>e.Action=="FundModelPackageExported").OrderByDescending(e=>e.Id).ToListAsync();
        foreach(var entry in audit) {
            using var doc=JsonDocument.Parse(entry.DetailJson);if(doc.RootElement.GetProperty("packageId").GetString()!=id)continue;
            var name=Path.GetFileName(doc.RootElement.GetProperty("name").GetString()!);var path=Path.Combine(StorageRoot,name);
            return File.Exists(path)?(File.OpenRead(path),name):null;
        }
        return null;
    }
    private static FundModelPackage Read(byte[] bytes,string passphrase)
    {
        try {var p=JsonSerializer.Deserialize<FundModelPackage>(FundModelTransferEncryption.Decrypt(bytes,passphrase),FundModelJson.Options);Require(p is not null,"Empty package.");Validate(p!);return p!;}
        catch(Exception ex) when(ex is JsonException or NullReferenceException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException) {throw new ValidationException("Invalid fund modelling package: unsupported or malformed data.");}
    }
    public async Task<FundModelTransferPreview> PreviewAsync(byte[] bytes,string passphrase,ClaimsPrincipal user)
    {
        await using var db=await factory.CreateDbContextAsync();await AuthorizeAsync(db,user);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var preview=await InspectAsync(db,Read(bytes,passphrase));await tx.CommitAsync();return preview;
    }
    internal static string ActionFor(string? current,string incoming,string? baseline) => current is null?"Create":current==incoming?"Unchanged":baseline==current?"Update transferred record":"Conflict";
    private static async Task<FundModelTransferPreview> InspectAsync(ApplicationDbContext db,FundModelPackage p)
    {
        var preview=new FundModelTransferPreview{Package=p};var stamps=new List<string>{Hash(p)};
        var funds=await db.ModelFunds.AsNoTracking().ToListAsync();var securityKeys=p.Securities.Select(s=>s.Key).ToList();var runKeys=p.Runs.Select(r=>r.Run.Key).ToList();
        var securities=await db.ModelSecurities.AsNoTracking().Where(MatchingKeys<ModelSecurity>(securityKeys)).ToListAsync();var runs=await db.ModelRuns.AsNoTracking().Where(MatchingKeys<ModelRun>(runKeys)).ToListAsync();
        var history=await db.ModelEvents.AsNoTracking().Where(e=>e.Action=="FundModelTransferBaseline").OrderBy(e=>e.Id).ToListAsync();
        var baselines=history.Select(e=>FundModelJson.Read<FundModelTransferBaseline>(e.DetailJson)).Where(b=>b.SourceSystem==p.SourceSystem).GroupBy(b=>(b.Kind,b.Key)).ToDictionary(g=>g.Key,g=>g.Last().Digest);
        void Row(string kind,string key,string name,string? current,string incoming,string stamp) {
            baselines.TryGetValue((kind,key),out var baseline);var action=ActionFor(current,incoming,baseline);
            preview.Rows.Add(new(kind,key,name,action));stamps.Add(Hash(new{kind,key,current,stamp}));
            if(action=="Conflict")preview.Conflicts.Add($"{kind} '{name}' differs on live and has no unchanged transfer baseline. Live edits will not be overwritten.");
        }
        foreach(var f in p.Funds) {
            var same=funds.Where(t=>t.Key.Equals(f.Key,StringComparison.OrdinalIgnoreCase)).ToList();Require(same.Count<=1,"Ambiguous fund key on live.");var target=same.SingleOrDefault();
            if(target is not null&&target.Key!=f.Key)preview.Conflicts.Add($"Fund key '{f.Key}' differs in case from live '{target.Key}'. Resolve the key before import.");
            if(funds.Any(t=>t.Key!=f.Key&&t.Name.Equals(f.Name,StringComparison.OrdinalIgnoreCase)))preview.Conflicts.Add($"Fund name '{f.Name}' already exists with another stable key. Resolve the identity before import.");
            Row("Fund",f.Key,f.Name,target is null?null:FundDigest(target),FundDigest(f),target?.Revision??"");
        }
        foreach(var s in p.Securities) {
            var same=securities.Where(t=>t.Key.Equals(s.Key,StringComparison.OrdinalIgnoreCase)).ToList();Require(same.Count<=1,"Ambiguous security key on live.");var target=same.SingleOrDefault();
            if(target is not null&&target.Key!=s.Key)preview.Conflicts.Add($"Security key '{s.Key}' differs in case from live '{target.Key}'. Resolve the key before import.");
            Row("Security",s.Key,s.Name,target is null?null:SecurityDigest(target),SecurityDigest(s),target?.UpdatedUtc.ToString("O")??"");
            if(target is not null&&SecurityDigest(target)!=SecurityDigest(s)) {
                var outside=funds.Where(f=>!p.Funds.Any(incoming=>incoming.Key==f.Key)&&FundModelJson.Read<List<ModelHolding>>(f.HoldingsJson).Any(h=>h.Security==target.Key)).Select(f=>f.Name).ToList();
                if(outside.Count>0)preview.Conflicts.Add($"Shared security '{s.Key}' also belongs to {string.Join(", ",outside)}. Include those funds or reconcile the shared history before changing it.");
            }
        }
        foreach(var item in p.Runs) {
            var target=runs.SingleOrDefault(r=>r.Key.Equals(item.Run.Key,StringComparison.OrdinalIgnoreCase));
            if(target is not null&&target.Key!=item.Run.Key)preview.Conflicts.Add($"Run key '{item.Run.Key}' differs in case from live '{target.Key}'. Resolve the key before import.");
            if(target is not null) {
                var fund=funds.Single(f=>f.Id==target.FundId);
                var equal=RunDigest(fund.Key,target)==RunDigest(item.FundKey,item.Run)&&target.State is "Complete" or "Deleted";
                Row("Run",item.Run.Key,$"Source run #{item.SourceRunId}",equal?RunDigest(item.FundKey,item.Run):"different",RunDigest(item.FundKey,item.Run),target.State);
            } else Row("Run",item.Run.Key,$"Source run #{item.SourceRunId}",null,RunDigest(item.FundKey,item.Run),"");
        }
        preview.Stamp=Hash(stamps);return preview;
    }
    public async Task<int> ApplyAsync(byte[] bytes,string passphrase,string stamp,string reason,ClaimsPrincipal user)
    {
        Require(!string.IsNullOrWhiteSpace(reason)&&reason.Length<=2000,"Enter an import reason (maximum 2,000 characters).");
        var p=Read(bytes,passphrase);
        await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);var actor=await AuthorizeAsync(db,user);
        var preview=await InspectAsync(db,p);Require(preview.CanApply,string.Join(" ",preview.Conflicts));Require(stamp==preview.Stamp,"Package or live records changed after preview. Preview again before applying.");
        var changed=0;
        void Baseline(string kind,string key,string digest) => FundModelService.Audit(db,null,"FundModelTransferBaseline",actor,new FundModelTransferBaseline(p.SourceSystem,kind,key,digest));
        foreach(var source in p.Securities) {
            var target=await db.ModelSecurities.SingleOrDefaultAsync(s=>s.Key==source.Key);
            if(target is null){target=new(){Key=source.Key};db.ModelSecurities.Add(target);}
            if(SecurityDigest(target)!=SecurityDigest(source)) {target.Name=source.Name;target.Currency=source.Currency;target.ProviderSymbol=source.ProviderSymbol;target.SeriesJson=source.SeriesJson;target.UpdatedUtc=DateTime.UtcNow;changed++;}
            Baseline("Security",source.Key,SecurityDigest(source));
        }
        var map=new Dictionary<string,ModelFund>();
        foreach(var source in p.Funds) {
            var target=await db.ModelFunds.SingleOrDefaultAsync(f=>f.Key==source.Key);
            if(target is null){target=new(){Key=source.Key};db.ModelFunds.Add(target);}
            if(FundDigest(target)!=FundDigest(source)) {target.Name=source.Name;target.Currency=source.Currency;target.Archived=source.Archived;target.CashValue=source.CashValue;target.HoldingsJson=source.HoldingsJson;target.ScenariosJson=source.ScenariosJson;target.Revision=Guid.NewGuid().ToString("N");target.UpdatedUtc=DateTime.UtcNow;changed++;}
            map[source.Key]=target;Baseline("Fund",source.Key,FundDigest(source));
        }
        await db.SaveChangesAsync();
        var runMap=new List<object>();
        foreach(var item in p.Runs) {
            var run=await db.ModelRuns.SingleOrDefaultAsync(r=>r.Key==item.Run.Key);
            if(run is null) {run=FundModelJson.Read<ModelRun>(FundModelJson.Write(item.Run));run.Id=0;run.FundId=map[item.FundKey].Id;run.CancelRequested=false;db.ModelRuns.Add(run);changed++;await db.SaveChangesAsync();}
            runMap.Add(new{item.SourceFundId,item.SourceRunId,liveFundId=run.FundId,liveRunId=run.Id,run.Key});
        }
        FundModelService.Audit(db,null,"FundModelPackageApplied",actor,new{p.PackageId,p.SourceSystem,p.ExportedBy,p.CreatedUtc,reason,changed,runMap});
        await db.SaveChangesAsync();await tx.CommitAsync();return changed;
    }

    internal static void Validate(FundModelPackage p)
    {
        Require(p.Version==1&&Guid.TryParseExact(p.PackageId,"N",out _)&&Regex.IsMatch(p.SourceSystem,"^[A-F0-9]{64}$"),"Unsupported fund modelling package version or identity.");
        Require(!string.IsNullOrWhiteSpace(p.ExportedBy)&&p.ExportedBy.Length<=256&&!string.IsNullOrWhiteSpace(p.Reason)&&p.Reason.Length<=2000,"Missing export provenance.");
        Require(p.Funds.Count is >0 and <=25&&p.Securities.Count<=1500&&p.Runs.Count<=500,"Package record limits exceeded.");
        Require(p.Funds.Select(f=>f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()==p.Funds.Count&&p.Securities.Select(s=>s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()==p.Securities.Count&&p.Runs.Select(r=>r.Run.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()==p.Runs.Count,"Duplicate stable keys in package.");
        foreach(var s in p.Securities) {
            Require(Regex.IsMatch(s.Key,"^[A-Za-z0-9._:-]{1,100}$")&&!string.IsNullOrWhiteSpace(s.Name)&&s.Name.Length<=200&&Regex.IsMatch(s.Currency,"^[A-Z]{3}$")&&s.ProviderSymbol.Length<=50,"Invalid security metadata.");
            var rows=FundModelJson.Read<List<ModelObservation>>(s.SeriesJson);Require(rows.Count<=2400&&rows.Select(r=>r.Month).Distinct().Count()==rows.Count,"Duplicate or excessive price months.");
            foreach(var row in rows)Require(Regex.IsMatch(row.Month,"^[0-9]{4}-(0[1-9]|1[0-2])$")&&double.IsFinite(row.Price)&&row.Price>0&&(row.TotalReturnIndex is null||double.IsFinite(row.TotalReturnIndex.Value)&&row.TotalReturnIndex>0)&&!string.IsNullOrWhiteSpace(row.Source),"Invalid price observation.");
        }
        foreach(var f in p.Funds) {
            Require(Regex.IsMatch(f.Key,"^[A-Za-z0-9_-]{1,64}$")&&!string.IsNullOrWhiteSpace(f.Name)&&f.Name.Length<=160&&Regex.IsMatch(f.Currency,"^[A-Z]{3}$")&&double.IsFinite(f.CashValue)&&f.CashValue>=0,"Invalid fund metadata.");
            var holdings=FundModelJson.Read<List<ModelHolding>>(f.HoldingsJson);Require(holdings.Count<=150&&holdings.Select(h=>h.Security).Distinct().Count()==holdings.Count,"Invalid fund holdings.");
            foreach(var h in holdings)Require(p.Securities.Any(s=>s.Key==h.Security)&&double.IsFinite(h.Value)&&h.Value>=0&&double.IsFinite(h.Minimum)&&double.IsFinite(h.Maximum)&&h.Minimum>=0&&h.Minimum<=h.Maximum&&h.Maximum<=1,"Holding has invalid limits or missing security.");
            var scenarios=FundModelJson.Read<List<ModelScenario>>(f.ScenariosJson);Require(scenarios.Count<=30&&scenarios.Select(s=>s.Name).Distinct().Count()==scenarios.Count,"Invalid saved scenarios.");
            foreach(var scenario in scenarios)ValidateScenario(scenario);
        }
        foreach(var item in p.Runs) {
            var r=item.Run;Require(p.Funds.Any(f=>f.Key==item.FundKey)&&r.State=="Complete"&&Regex.IsMatch(r.Key,"^[A-Za-z0-9_-]{1,64}$")&&r.Actor.Length<=256&&r.Progress.Length<=400&&r.Fingerprint==FundModelService.Hash(r.InputJson),"Invalid completed run identity or fingerprint.");
            using var input=JsonDocument.Parse(r.InputJson);using var result=JsonDocument.Parse(r.ResultJson);
            var i=input.RootElement;var o=result.RootElement;Require(i.GetProperty("version").GetString()=="markowitz-1"&&o.GetProperty("version").GetString()=="markowitz-1","Unsupported calculation version.");
            Require(!string.IsNullOrWhiteSpace(i.GetProperty("name").GetString()),"Missing frozen fund name.");
            ValidateScenario(i.GetProperty("scenario").Deserialize<ModelScenario>(FundModelJson.Options)!);
            foreach(var security in i.GetProperty("securities").EnumerateArray()) {
                Require(!string.IsNullOrWhiteSpace(security.GetProperty("key").GetString())&&!string.IsNullOrWhiteSpace(security.GetProperty("name").GetString())&&Regex.IsMatch(security.GetProperty("currency").GetString()!,"^[A-Z]{3}$"),"Invalid frozen security.");
                var observations=security.GetProperty("series").Deserialize<List<ModelObservation>>(FundModelJson.Options)!;
                Require(observations.Count<=2400,"Excessive frozen price history.");
                foreach(var observation in observations)Require(Regex.IsMatch(observation.Month,"^[0-9]{4}-(0[1-9]|1[0-2])$")&&observation.Price>0&&double.IsFinite(observation.Price)&&!string.IsNullOrWhiteSpace(observation.Source),"Invalid frozen price history.");
            }
            var keys=o.GetProperty("keys").EnumerateArray().Select(k=>k.GetString()).ToArray();Require(keys.Length is >1 and <=151&&keys.Distinct().Count()==keys.Length&&keys[^1]=="CASH","Invalid result securities.");
            Require(o.GetProperty("annual").GetArrayLength()>0&&o.GetProperty("monthly").GetArrayLength()<=600,"Invalid result periods.");
            foreach(var a in o.GetProperty("annual").EnumerateArray()) {Require(a.GetProperty("weights").GetArrayLength()==keys.Length&&a.GetProperty("historyMonths").GetArrayLength()==keys.Length-1,"Invalid result weights.");_=a.GetProperty("expectedReturn").GetDouble();_=a.GetProperty("estimatedRisk").GetDouble();_=a.GetProperty("excluded").GetArrayLength();_=a.GetProperty("month").GetString();}
            Require(o.GetProperty("currentWeights").GetArrayLength()==keys.Length,"Invalid current portfolio weights.");
            foreach(var key in new[]{"currentRisk","currentReturn","totalReturn","maxDrawdown"})_=o.GetProperty(key).GetDouble();
            foreach(var key in new[]{"cagr","volatility"})Require(o.GetProperty(key).ValueKind is JsonValueKind.Number or JsonValueKind.Null,"Invalid result summary.");
            foreach(var m in o.GetProperty("monthly").EnumerateArray()) {_=m.GetProperty("month").GetString();foreach(var key in new[]{"net","gross","cost","wealth","equalNet","drawdown"})_=m.GetProperty(key).GetDouble();}
            foreach(var f in o.GetProperty("frontier").EnumerateArray()){_=f.GetProperty("risk").GetDouble();_=f.GetProperty("expectedReturn").GetDouble();}
            _=o.GetProperty("warnings").GetArrayLength();_=i.GetProperty("securities").GetArrayLength();_=i.GetProperty("scenario").GetProperty("basis").GetString();
        }
    }
    private static void ValidateScenario(ModelScenario s)
    {
        Require(!string.IsNullOrWhiteSpace(s.Name)&&s.Name.Length<=200&&new[]{s.Risk,s.Cap,s.CashMin,s.CashMax,s.CashAnnual,s.CovShrink,s.MeanShrink,s.Cost}.All(double.IsFinite),"Invalid saved scenario values.");
        Require(s.Risk>=0&&s.Risk<=1&&s.Cap>=0&&s.Cap<=1&&s.CashMin>=0&&s.CashMin<=s.CashMax&&s.CashMax<=1&&s.CovShrink>=0&&s.CovShrink<=1&&s.MeanShrink>=0&&s.MeanShrink<=1&&s.Cost>=0&&s.Cost<=1&&s.CashAnnual> -.99&&s.CashAnnual<=1,"Invalid saved scenario constraints.");
        Require(s.Window>=s.MinimumHistory&&s.MinimumHistory>=12&&s.Window<=240&&s.RebalanceMonths is 1 or 3 or 6 or 12&&s.Mode is "Optimise" or "Backtest"&&s.Basis is "Price" or "TotalReturn"&&s.Universe is "Selected" or "Dated","Unsupported saved scenario settings.");
        Require(Regex.IsMatch(s.Start,"^[0-9]{4}-(0[1-9]|1[0-2])$")&&Regex.IsMatch(s.End,"^[0-9]{4}-(0[1-9]|1[0-2])$")&&string.CompareOrdinal(s.Start,s.End)<=0,"Invalid saved scenario dates.");
    }
}
