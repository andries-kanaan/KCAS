using KCAS.Admin.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

public sealed class GoAmlWeekendReminderTests
{
    [Theory]
    [InlineData("2026-11-07T06:00:00Z")]
    [InlineData("2026-11-08T06:00:00Z")]
    [InlineData("2026-11-06T22:30:00Z")] // Friday UTC is already Saturday in South Africa.
    [InlineData("2026-11-08T21:59:00Z")]
    public async Task South_African_weekends_do_not_send_or_touch_delivery_records(string utc)
    {
        var service = new GoAmlReminderService(null!, Options.Create(new GoAmlReminderOptions
        {
            Enabled = true, Recipient = "checker@example.test", PublicBaseUrl = "https://live.example.test", HourLocal = 0
        }), new NeverSend(), new Clock(DateTimeOffset.Parse(utc)), NullLogger<GoAmlReminderService>.Instance);
        // A null database and throwing sender ensure weekends return before either is touched.
        await service.RunAsync();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NeverSend : IGoAmlEmailSender
    {
        public string? ConfigurationProblem => null;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Weekend email must not be sent.");
    }
}
