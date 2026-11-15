using KCAS.Admin.Data;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml.Linq;

namespace KCAS.Admin.Tests;

public class FundModelTests
{
    [Fact]
    public void SavedPeriodsUseLatestSavedMonthAndNeverInventMissingEndpoints()
    {
        var rows=new[]{new ModelObservation{Month="2019-02",Price=100},new ModelObservation{Month="2021-10",Price=105},new ModelObservation{Month="2023-10",Price=110},new ModelObservation{Month="2026-10",Price=120}};
        Assert.Equal(("2023-10","2026-10"),FundModelPresentation.SavedPeriod(rows,36)!.Value);
        Assert.Equal(("2021-10","2026-10"),FundModelPresentation.SavedPeriod(rows,60)!.Value);
        Assert.Equal(("2019-02","2026-10"),FundModelPresentation.SavedPeriod(rows,null)!.Value);
        Assert.Null(FundModelPresentation.SavedPeriod(rows,48));
        Assert.Null(FundModelPresentation.SavedPeriod([],null));
        Assert.Null(FundModelPresentation.SavedPeriod(rows,0));
    }
    [Fact]
    public async Task HistoryOpensRequestedSecurityRatherThanFirstAndUpdatesSelection()
    {
        var securities = new List<ModelSecurity> {
            new() { Key="FIRST",Name="First security",SeriesJson=FundModelJson.Write(new[]{new ModelObservation{Month="2020-01",Price=100}}) },
            new() { Key="SECOND",Name="Requested security",SeriesJson=FundModelJson.Write(new[]{new ModelObservation{Month="2024-01",Price=200},new ModelObservation{Month="2026-01",Price=242}}) }
        };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        await using var provider=Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        await using var renderer=new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider,
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>(provider));
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var component=await renderer.RenderComponentAsync<KCAS.Admin.Components.Shared.FundSecurityReturns>(Microsoft.AspNetCore.Components.ParameterView.FromDictionary(
                new Dictionary<string,object?>{{"Securities",securities},{"SelectedKey","SECOND"}}));
            var html=component.ToHtmlString();
            Assert.Contains("Requested security ·",html);
            Assert.Contains("242.0000",html);
            Assert.Contains("24 months",html);
            Assert.DoesNotContain("First security ·",html);
            await component.QuiescenceTask;
        });
    }
    [Fact]
    public void CarUsesSelectedEndpointsAndCalendarMonthsNotRowCount()
    {
        var rows=new[]{new ModelObservation{Month="2024-01",Price=100,TotalReturnIndex=100},new ModelObservation{Month="2026-01",Price=121,TotalReturnIndex=144}};
        var price=FundModelPresentation.ReturnSince(rows,"2024-01","2026-01",false)!;
        Assert.Equal(24,price.Months);Assert.Equal(.21,price.Growth,10);Assert.Equal(.1,price.Car,10);
        Assert.Equal(.2,FundModelPresentation.ReturnSince(rows,"2024-01","2026-01",true)!.Car,10);
        Assert.Null(FundModelPresentation.ReturnSince(rows,"2026-01","2024-01",false));
        Assert.Null(FundModelPresentation.ReturnSince(rows,"2024-01","2024-01",false));
        Assert.Null(FundModelPresentation.ReturnSince(rows,"2024-01","2025-01",false));
    }
    [Fact]
    public void CarDoesNotSubstitutePriceForMissingTotalReturn()
    {
        var rows=new[]{new ModelObservation{Month="2025-12",Price=100},new ModelObservation{Month="2026-06",Price=110}};
        Assert.Equal(.21,FundModelPresentation.ReturnSince(rows,"2025-12","2026-06",false)!.Car,10);
        Assert.Null(FundModelPresentation.ReturnSince(rows,"2025-12","2026-06",true));
    }
    [Theory]
    [InlineData("Complete",true)]
    [InlineData("Failed",true)]
    [InlineData("Cancelled",true)]
    [InlineData("Interrupted",true)]
    [InlineData("Running",false)]
    [InlineData("Queued",false)]
    [InlineData("Deleted",false)]
    public void DeletionOnlyForStoppedRuns(string state,bool expected)=>Assert.Equal(expected,FundModelService.CanDeleteRun(state));
    [Fact]
    public void ReturnsUsePrecedingCalendarMonthAndSelectedBasis()
    {
        var rows=new[]{new ModelObservation{Month="2025-12",Price=100,TotalReturnIndex=100},new ModelObservation{Month="2026-01",Price=105,TotalReturnIndex=107},new ModelObservation{Month="2026-03",Price=110}};
        Assert.Equal(.05,FundModelPresentation.MonthlyReturn(rows,"2026-01",false)!.Value,10);
        Assert.Equal(.07,FundModelPresentation.MonthlyReturn(rows,"2026-01",true)!.Value,10);
        Assert.Null(FundModelPresentation.MonthlyReturn(rows,"2026-03",false));
        Assert.Null(FundModelPresentation.MonthlyReturn(rows,"2026-03",true));
    }
    [Fact]
    public void ChartScaleIncludesNegativeReturnsAndOutlyingCurrentPortfolio()
    {
        var axis=FundModelPresentation.Axis(new[]{-.07,.13,.51});
        Assert.True(axis.Minimum<=-.07);Assert.True(axis.Maximum>=.51);Assert.True(axis.Step>0);
        var zero=FundModelPresentation.Axis(new[]{0d},true);Assert.True(zero.Maximum>zero.Minimum);
    }
    [Fact]
    public void PricesKeepMissingTotalReturnAndSource()
    {
        var rows=FundModelService.ParsePrices("Month,Price,TotalReturnIndex,Source\n2026-01,100,,Administrator Jan statement\n2026-02,101,102,Official factsheet");
        Assert.Null(rows[0].TotalReturnIndex); Assert.Equal(102,rows[1].TotalReturnIndex);Assert.Equal("Official factsheet",rows[1].Source);
    }
    [Theory]
    [InlineData("2026-01,0,,source")]
    [InlineData("2026-13,100,,source")]
    [InlineData("2026-01,NaN,,source")]
    [InlineData("2026-01,100,,")]
    public void InvalidPriceRowsFail(string row)=>Assert.Throws<ValidationException>(()=>FundModelService.ParsePrices("Month,Price,TotalReturnIndex,Source\n"+row));
    [Fact]
    public void DuplicateMonthsFail()=>Assert.Throws<ValidationException>(()=>FundModelService.ParsePrices("Month,Price,TotalReturnIndex,Source\n2026-01,100,,source\n2026-01,102,,source"));
}
