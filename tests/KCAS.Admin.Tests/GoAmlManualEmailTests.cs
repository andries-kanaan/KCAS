using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

public sealed class GoAmlManualEmailTests
{
    [Theory]
    [InlineData("https://kcas.test:8443")]
    [InlineData("https://kcas:8443")]
    public async Task Explicit_manual_send_works_with_schedule_off_and_without_database_access(string url)
    {
        var sender = new Sender();
        var service = Create(sender, url);
        var result = await service.SendManualAsync(User(true));
        Assert.Equal("checker@example.test", sender.Recipient);
        Assert.Contains(new Uri(url).Host, sender.Subject);
        Assert.Contains(url + "/compliance/goaml", sender.Body);
        Assert.Contains("https://kcas:8443/compliance/goaml", sender.Body);
        Assert.Contains("https://kcas.test:8443/compliance/goaml", sender.Body);
        var other = url.Contains("kcas.test") ? "https://kcas:8443" : "https://kcas.test:8443";
        Assert.True(sender.Body.IndexOf(url + "/compliance/goaml", StringComparison.Ordinal) <
            sender.Body.IndexOf(other + "/compliance/goaml", StringComparison.Ordinal));
        Assert.Contains("requested manually", sender.Body);
        Assert.Contains("accepted", result);
    }

    [Fact]
    public async Task Unauthorised_or_unconfigured_request_does_not_send()
    {
        var sender = new Sender();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(sender).SendManualAsync(User(false)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(sender).SendManualAsync(new ClaimsPrincipal()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(sender, "http://unsafe.test").SendManualAsync(User(true)));
        sender.ConfigurationProblem = "Missing sender.";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(sender).SendManualAsync(User(true)));
        Assert.Equal(0, sender.Attempts);
    }

    [Fact]
    public async Task Failed_send_reports_uncertainty_without_retry_or_leaking_provider_error()
    {
        var sender = new Sender { Fail = true };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(sender).SendManualAsync(User(true)));
        Assert.Contains("could not be confirmed", error.Message);
        Assert.DoesNotContain("private provider error", error.Message);
        Assert.Equal(1, sender.Attempts);
    }

    private static ClaimsPrincipal User(bool manage) => new(new ClaimsIdentity(manage
        ? [new Claim(ClaimTypes.Name, "checker@example.test"), new Claim(KcasClaimTypes.Permission, KcasPermissions.ComplianceManage)]
        : [new Claim(ClaimTypes.Name, "viewer@example.test")], "test"));

    private static GoAmlReminderService Create(Sender sender, string url = "https://kcas.test:8443")
    {
        var options = new GoAmlReminderOptions { Enabled = false, Recipient = "checker@example.test", PublicBaseUrl = url };
        if (url.Contains("kcas.test"))
        {
            options.PublicBaseUrl = "https://kcas:8443";
            options.ApplyEnvironment("Development");
        }
        else options.ApplyEnvironment("Production");
        return new(null!, Options.Create(options), sender, TimeProvider.System, NullLogger<GoAmlReminderService>.Instance);
    }

    private sealed class Sender : IGoAmlEmailSender
    {
        public string? ConfigurationProblem { get; set; }
        public string Recipient = "", Subject = "", Body = "";
        public int Attempts;
        public bool Fail;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Fail) throw new IOException("private provider error");
            (Recipient, Subject, Body) = (recipient, subject, body);
            return Task.CompletedTask;
        }
    }
}
