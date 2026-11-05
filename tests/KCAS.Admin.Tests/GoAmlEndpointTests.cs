using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class GoAmlEndpointTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Portal_open_requires_manage_permission_and_valid_antiforgery_before_recording()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "GoAmlTest";
                options.DefaultChallengeScheme = "GoAmlTest";
                options.DefaultForbidScheme = "GoAmlTest";
            }).AddScheme<AuthenticationSchemeOptions, GoAmlTestAuthentication>("GoAmlTest", _ => { })));
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var viewHtml = await client.GetStringAsync("/compliance/goaml");
        var viewToken = Regex.Match(viewHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(viewToken.Success);
        var forbidden = await client.PostAsync("/compliance/goaml/open", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(viewToken.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Add("X-GoAml-Test-Manage", "true");
        var csrf = await client.PostAsync("/compliance/goaml/open", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var html = await client.GetStringAsync("/compliance/goaml");
        Assert.Contains("action=\"/compliance/goaml/open\"", html);
        Assert.DoesNotContain("type=\"file\"", html);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var opened = await client.PostAsync("/compliance/goaml/open", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.StartsWith("https://", opened.Headers.Location!.AbsoluteUri);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(GoAmlClock.LocalNow);
        var check = await db.GoAmlDailyChecks.AsNoTracking().SingleAsync(x => x.CheckDate == today);
        Assert.Equal(GoAmlCheckStatuses.AccessRecorded, check.Status);
        Assert.Equal("goaml-endpoint@example.test", check.CompletedBy);
        Assert.Null(check.EvidencePath);
    }
}

public sealed class GoAmlTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        // A body prevents the application's status-page re-execution from handling this POST as a Blazor form.
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "text/plain";
        return Response.WriteAsync("Permission denied.");
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "goaml-endpoint@example.test"),
            new(KcasClaimTypes.Permission, KcasPermissions.ComplianceView)
        };
        if (Request.Headers.ContainsKey("X-GoAml-Test-Manage")) claims.Add(new(KcasClaimTypes.Permission, KcasPermissions.ComplianceManage));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
