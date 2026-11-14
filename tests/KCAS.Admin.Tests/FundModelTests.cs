using KCAS.Admin.Data;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml.Linq;

namespace KCAS.Admin.Tests;

public class FundModelTests
{
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
