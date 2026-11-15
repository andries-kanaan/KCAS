using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KCAS.Admin.Tests;

public class FundModelTransferUnitTests
{
    [Theory]
    [InlineData(null,"new",null,"Create")]
    [InlineData("same","same",null,"Unchanged")]
    [InlineData("old","new","old","Update transferred record")]
    [InlineData("live edit","new","old","Conflict")]
    [InlineData("existing","new",null,"Conflict")]
    public void ThreeWayUpdatesPreserveLiveEdits(string? current,string incoming,string? baseline,string expected)=>Assert.Equal(expected,FundModelTransferService.ActionFor(current,incoming,baseline));
    [Fact]
    public void EncryptionAuthenticatesPasswordPayloadAndPurpose()
    {
        var source=Encoding.UTF8.GetBytes("Confidential research package");
        var bytes=FundModelTransferEncryption.Encrypt(source,"TestPwd7");
        Assert.Equal(source,FundModelTransferEncryption.Decrypt(bytes,"TestPwd7"));
        Assert.DoesNotContain("Confidential research",Encoding.UTF8.GetString(bytes));
        Assert.Throws<ValidationException>(()=>FundModelTransferEncryption.Decrypt(bytes,"WrongPwd7"));
        bytes[^1]^=1;Assert.Throws<ValidationException>(()=>FundModelTransferEncryption.Decrypt(bytes,"TestPwd7"));
        Assert.Throws<ValidationException>(()=>FundModelTransferEncryption.Decrypt(EmployeeTransferEncryption.Encrypt(source,"TestPwd7"),"TestPwd7"));
        Assert.Throws<ValidationException>(()=>FundModelTransferEncryption.Decrypt([1,2,3],"TestPwd7"));
    }
}

[Collection(KcasTestCollection.Name)]
public class FundModelTransferIntegrationTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task RoundtripMapsIdsPreservesRunsRepeatsAndChecksPreviewAndLiveConflicts()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user=await Admin(scope);var directory=Path.Combine(Path.GetTempPath(),"kcas-fund-transfer-"+Guid.NewGuid().ToString("N"));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"FundModelling:TransferStorageRoot",directory}}).Build();
        var service=new FundModelTransferService(scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),config,scope.ServiceProvider.GetRequiredService<IHostEnvironment>());
        var key=Guid.NewGuid().ToString("N");var fund=new ModelFund{Key=key,Name="Transfer test "+key,UpdatedUtc=DateTime.UtcNow};
        var security=new ModelSecurity{Key="T"+key,Name="Synthetic security",SeriesJson=FundModelJson.Write(new[]{new ModelObservation{Month="2026-01",Price=100,Source="Synthetic statement"}})};
        fund.HoldingsJson=FundModelJson.Write(new[]{new ModelHolding{Security=security.Key,Value=100}});
        db.ModelFunds.Add(fund);db.ModelSecurities.Add(security);await db.SaveChangesAsync();
        var input=FundModelJson.Write(new{version="markowitz-1",name=fund.Name,scenario=new ModelScenario{Mode="Optimise"},securities=new[]{new{key=security.Key,name=security.Name,currency="USD",series=FundModelJson.Read<List<ModelObservation>>(security.SeriesJson)}}});
        var result=FundModelJson.Write(new{version="markowitz-1",keys=new[]{security.Key,"CASH"},annual=new[]{new{month="2026-02",weights=new[]{.15,.85},historyMonths=new[]{60},excluded=Array.Empty<object>(),expectedReturn=.02,estimatedRisk=.01}},monthly=Array.Empty<object>(),frontier=Array.Empty<object>(),currentWeights=new[]{1d,0d},currentRisk=.1,currentReturn=.1,totalReturn=0d,maxDrawdown=0d,cagr=(double?)null,volatility=(double?)null,warnings=new[]{"Synthetic test"}});
        var run=new ModelRun{FundId=fund.Id,State="Complete",InputJson=input,ResultJson=result,Fingerprint=FundModelService.Hash(input),Actor="Source researcher",CreatedUtc=DateTime.UtcNow,FinishedUtc=DateTime.UtcNow};
        db.ModelRuns.Add(run);await db.SaveChangesAsync();var sourceId=fund.Id;var sourceRun=run.Id;
        var exported=await service.ExportAsync([fund.Id],true,"TestPwd7","Synthetic export",user);
        var download=await service.DownloadAsync(exported.PackageId,user);Assert.NotNull(download);byte[] bytes;
        await using(var stream=download.Value.Content){using var buffer=new MemoryStream();await stream.CopyToAsync(buffer);bytes=buffer.ToArray();}
        Assert.Equal(1,exported.Runs);Assert.DoesNotContain(fund.Name,Encoding.UTF8.GetString(bytes));
        // Source-only records are removed to model live with different database IDs.
        db.ModelRuns.Remove(run);db.ModelFunds.Remove(fund);db.ModelSecurities.Remove(security);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var preview=await service.PreviewAsync(bytes,"TestPwd7",user);Assert.True(preview.CanApply,string.Join(" ",preview.Conflicts));
        Assert.Equal(3,await service.ApplyAsync(bytes,"TestPwd7",preview.Stamp,"Synthetic live import",user));
        var live=await db.ModelFunds.AsNoTracking().SingleAsync(f=>f.Key==key);var liveRun=await db.ModelRuns.AsNoTracking().SingleAsync(r=>r.Key==run.Key);
        Assert.NotEqual(sourceId,live.Id);Assert.NotEqual(sourceRun,liveRun.Id);Assert.Equal(live.Id,liveRun.FundId);Assert.Equal(input,liveRun.InputJson);Assert.Equal(result,liveRun.ResultJson);Assert.Equal("Complete",liveRun.State);
        Assert.NotEmpty(FundModelReports.Excel(liveRun));Assert.NotEmpty(FundModelReports.Pdf(liveRun));
        var repeated=await service.PreviewAsync(bytes,"TestPwd7",user);Assert.All(repeated.Rows,r=>Assert.Equal("Unchanged",r.Action));Assert.Equal(0,await service.ApplyAsync(bytes,"TestPwd7",repeated.Stamp,"Repeat",user));
        // A source update is supported only while the imported live baseline is unchanged.
        var package=FundModelJson.Read<FundModelPackage>(Encoding.UTF8.GetString(FundModelTransferEncryption.Decrypt(bytes,"TestPwd7")));
        package.PackageId=Guid.NewGuid().ToString("N");package.Funds[0].CashValue=50;
        var update=FundModelTransferEncryption.Encrypt(Encoding.UTF8.GetBytes(FundModelJson.Write(package)),"TestPwd7");
        var ready=await service.PreviewAsync(update,"TestPwd7",user);Assert.True(ready.CanApply);Assert.Contains(ready.Rows,r=>r.Kind=="Fund"&&r.Action=="Update transferred record");
        await db.ModelFunds.Where(f=>f.Id==live.Id).ExecuteUpdateAsync(u=>u.SetProperty(f=>f.Revision,Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<ValidationException>(()=>service.ApplyAsync(update,"TestPwd7",ready.Stamp,"Stale preview",user));
        ready=await service.PreviewAsync(update,"TestPwd7",user);Assert.Equal(1,await service.ApplyAsync(update,"TestPwd7",ready.Stamp,"Update",user));
        // A changed shared history must not affect a fund outside this package.
        var outside=new ModelFund{Name="Outside "+key,HoldingsJson=fund.HoldingsJson};db.ModelFunds.Add(outside);await db.SaveChangesAsync();
        package.Securities[0].Name="Revised security";
        var shared=FundModelTransferEncryption.Encrypt(Encoding.UTF8.GetBytes(FundModelJson.Write(package)),"TestPwd7");
        var sharedPreview=await service.PreviewAsync(shared,"TestPwd7",user);Assert.Contains(sharedPreview.Conflicts,c=>c.Contains("Shared security"));
        await Assert.ThrowsAsync<ValidationException>(()=>service.ApplyAsync(shared,"TestPwd7",sharedPreview.Stamp,"Blocked shared update",user));
        db.ModelFunds.Remove(outside);await db.SaveChangesAsync();
        // Import must not resurrect a run explicitly deleted on live.
        await db.ModelRuns.Where(r=>r.Id==liveRun.Id).ExecuteUpdateAsync(u=>u.SetProperty(r=>r.State,"Deleted"));
        var deleted=await service.PreviewAsync(update,"TestPwd7",user);Assert.True(deleted.CanApply);Assert.Equal(0,await service.ApplyAsync(update,"TestPwd7",deleted.Stamp,"Repeat retaining deletion",user));
        Assert.Equal("Deleted",await db.ModelRuns.Where(r=>r.Id==liveRun.Id).Select(r=>r.State).SingleAsync());
        await db.ModelFunds.Where(f=>f.Id==live.Id).ExecuteUpdateAsync(u=>u.SetProperty(f=>f.CashValue,75));
        var blocked=await service.PreviewAsync(bytes,"TestPwd7",user);Assert.False(blocked.CanApply);Assert.Contains(blocked.Conflicts,c=>c.Contains("Live edits"));
        await Assert.ThrowsAsync<ValidationException>(()=>service.ApplyAsync(bytes,"TestPwd7",blocked.Stamp,"Blocked",user));
        var account=await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().RemoveFromRoleAsync(account!,KcasRoles.Administrator);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.PreviewAsync(bytes,"TestPwd7",user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.DownloadAsync(exported.PackageId,user));
        await db.ModelRuns.Where(r=>r.Id==liveRun.Id).ExecuteDeleteAsync();await db.ModelFunds.Where(f=>f.Id==live.Id).ExecuteDeleteAsync();await db.ModelSecurities.Where(s=>s.Key==security.Key).ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().DeleteAsync(account!);
    }
    private static async Task<ClaimsPrincipal> Admin(IServiceScope scope)
    {
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var email="fund-transfer-"+Guid.NewGuid().ToString("N")+"@example.test";
        var user=new ApplicationUser{Email=email,UserName=email,IsApproved=true};Assert.True((await users.CreateAsync(user)).Succeeded);Assert.True((await users.AddToRoleAsync(user,KcasRoles.Administrator)).Succeeded);
        return new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim(ClaimTypes.Name,email)},"Test"));
    }
}
