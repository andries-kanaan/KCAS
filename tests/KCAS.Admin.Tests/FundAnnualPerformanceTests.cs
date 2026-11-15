using System.Text.Json;
using KCAS.Admin.Data;
using KCAS.Admin.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KCAS.Admin.Tests;

public class FundAnnualPerformanceTests
{
    [Fact]
    public void CompoundsEachYearAndDoesNotAnnualisePartialPeriods()
    {
        var monthly = JsonSerializer.SerializeToElement(new[] {
            new {month="2023-02",gross=-.1,net=-.11,equalNet=-.2},
            new {month="2022-12",gross=.1,net=.09,equalNet=.2},
            new {month="2023-01",gross=.2,net=.19,equalNet=.1},
            new {month="2022-11",gross=.1,net=.1,equalNet=.1}
        });
        var rows=FundModelPresentation.AnnualReturns(monthly);
        Assert.Equal(2,rows.Count);
        Assert.Equal("2022-11",rows[0].From);
        Assert.Equal("2022-12",rows[0].To);
        Assert.Equal(2,rows[0].Months);
        Assert.False(rows[0].FullYear);
        Assert.Equal(.21,rows[0].Gross,10);
        Assert.Equal(.199,rows[0].Net,10);
        Assert.Equal(.32,rows[0].EqualMix,10);
        Assert.Equal(.0591,rows[1].Net,10);
        Assert.Empty(FundModelPresentation.AnnualReturns(JsonSerializer.SerializeToElement(Array.Empty<object>())));
    }
    [Fact]
    public async Task RendersFullAndPartialYearsAndFeeDistinction()
    {
        var monthly=JsonSerializer.SerializeToElement(Enumerable.Range(1,12).Select(m=>new {month=$"2023-{m:00}",gross=.01,net=.009,equalNet=.008})
            .Append(new {month="2024-01",gross=-.02,net=-.021,equalNet=-.01}));
        var rows=FundModelPresentation.AnnualReturns(monthly);
        Assert.True(rows[0].FullYear);Assert.False(rows[1].FullYear);
        await using var provider=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var rendered=await renderer.RenderComponentAsync<FundAnnualPerformance>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Monthly",monthly}}));
            var html=rendered.ToHtmlString();
            Assert.Contains("Annual performance",html);
            Assert.Contains("12 months</td>",html);
            Assert.Contains("1 month (partial year)",html);
            Assert.Contains("partial years are not annualised",html);
            Assert.Contains("exclude Kanaan management fees",html);
            var empty=await renderer.RenderComponentAsync<FundAnnualPerformance>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Monthly",JsonSerializer.SerializeToElement(Array.Empty<object>())}}));
            Assert.DoesNotContain("Annual performance",empty.ToHtmlString());
        });
    }
}
