using System.Net.Mail;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class GoAmlReminderServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Sends_once_at_South_African_hour_with_KCAS_link_and_persists_across_service_instances()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new TestClock(new DateTimeOffset(2070, 1, 9, 5, 59, 0, TimeSpan.Zero));
        var sender = new RecordingSender();
        var service = Create(db, sender, clock);
        await service.RunAsync();
        Assert.Empty(sender.Messages);
        clock.Now = clock.Now.AddMinutes(1); // 08:00 in South Africa, regardless of host timezone.
        await service.RunAsync();
        var message = Assert.Single(sender.Messages);
        Assert.Contains("https://live.example.test:8443/compliance/goaml", message);
        Assert.DoesNotContain(GoAmlDefaults.PortalUrl, message);
        Assert.False(await db.GoAmlDailyChecks.AnyAsync(x => x.CheckDate == new DateOnly(2070, 1, 9)));
        using var second = factory.Services.CreateScope();
        await Create(second.ServiceProvider.GetRequiredService<ApplicationDbContext>(), sender, clock).RunAsync();
        Assert.Single(sender.Messages);
        var status = await service.LoadStatusAsync();
        var saved = await db.GoAmlReminderDeliveries.AsNoTracking().SingleAsync(x => x.ReminderDate == new DateOnly(2070, 1, 9));
        Assert.Equal("Sent", saved.Status);
        Assert.Equal(clock.Now.UtcDateTime, saved.SentAtUtc);
        Assert.Equal("checker@example.test", saved.Recipient);
        Assert.Contains("08:00", status.Description);
        clock.Now = clock.Now.AddDays(1);
        await service.RunAsync();
        Assert.Equal(2, sender.Messages.Count);
    }

    [Fact]
    public async Task Disabled_or_unconfigured_sender_does_not_claim_delivery_and_completed_access_suppresses_reminder()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new TestClock(new DateTimeOffset(2070, 2, 12, 7, 0, 0, TimeSpan.Zero));
        var sender = new RecordingSender { ConfigurationProblem = "Sender missing." };
        var service = Create(db, sender, clock);
        await service.RunAsync();
        Assert.Equal("Sender missing.", (await service.LoadStatusAsync()).Description);
        Assert.False(await db.GoAmlReminderDeliveries.AnyAsync(x => x.ReminderDate == new DateOnly(2070, 2, 12)));
        sender.ConfigurationProblem = null;
        var disabled = Create(db, sender, clock, false);
        await disabled.RunAsync();
        Assert.Empty(sender.Messages);
        await scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>()
            .RecordAccessAsync("checker@example.test", clock.Now.UtcDateTime.AddHours(2));
        await service.RunAsync();
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Rejected_recipient_retries_after_thirty_minutes_but_ambiguous_delivery_never_silently_resends()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = new TestClock(new DateTimeOffset(2070, 3, 12, 7, 0, 0, TimeSpan.Zero));
        var sender = new RecordingSender { Failure = new SmtpFailedRecipientException("Rejected") };
        var service = Create(db, sender, clock);
        await service.RunAsync();
        await service.RunAsync();
        Assert.Equal(1, sender.Attempts);
        clock.Now = clock.Now.AddMinutes(30);
        sender.Failure = null;
        await service.RunAsync();
        Assert.Single(sender.Messages);
        var rejected = await db.GoAmlReminderDeliveries.AsNoTracking().SingleAsync(x => x.ReminderDate == new DateOnly(2070, 3, 12));
        Assert.Equal(2, rejected.Attempts);
        Assert.Equal("Sent", rejected.Status);
        clock.Now = clock.Now.AddDays(1);
        sender.Failure = new IOException("Connection interrupted.");
        await service.RunAsync();
        clock.Now = clock.Now.AddHours(1);
        sender.Failure = null;
        await service.RunAsync();
        Assert.Single(sender.Messages);
        var uncertain = await db.GoAmlReminderDeliveries.AsNoTracking().SingleAsync(x => x.ReminderDate == new DateOnly(2070, 3, 13));
        Assert.Equal("Uncertain", uncertain.Status);
        Assert.Null(uncertain.SentAtUtc);
    }

    [Fact]
    public async Task Concurrent_workers_only_claim_one_message()
    {
        var clock = new TestClock(new DateTimeOffset(2070, 4, 11, 7, 0, 0, TimeSpan.Zero));
        var sender = new RecordingSender();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        await Task.WhenAll(Create(first.ServiceProvider.GetRequiredService<ApplicationDbContext>(), sender, clock).RunAsync(),
            Create(second.ServiceProvider.GetRequiredService<ApplicationDbContext>(), sender, clock).RunAsync());
        Assert.Single(sender.Messages);
    }

    private static GoAmlReminderService Create(ApplicationDbContext db, RecordingSender sender, TestClock clock, bool enabled = true) =>
        new(db, Options.Create(new GoAmlReminderOptions { Enabled = enabled, Recipient = "checker@example.test",
            PublicBaseUrl = "https://live.example.test:8443", HourLocal = 8 }), sender, clock, NullLogger<GoAmlReminderService>.Instance);

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingSender : IGoAmlEmailSender
    {
        public string? ConfigurationProblem { get; set; }
        public List<string> Messages { get; } = [];
        public Exception? Failure { get; set; }
        public int Attempts { get; private set; }
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Failure is not null) throw Failure;
            Messages.Add(body);
            return Task.CompletedTask;
        }
    }
}
