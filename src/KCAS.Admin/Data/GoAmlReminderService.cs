using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace KCAS.Admin.Data;

public sealed class GoAmlReminderDelivery
{
    public int Id { get; set; }
    public DateOnly ReminderDate { get; set; }
    public string Recipient { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class GoAmlReminderOptions
{
    public bool Enabled { get; set; }
    public bool LocalEnabled { get; set; }
    public string LocalPublicBaseUrl { get; set; } = "https://kcas.test:8443";
    public string Recipient { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public int HourLocal { get; set; } = 8;

    public void ApplyEnvironment(string environmentName)
    {
        if (string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            Enabled = Enabled && LocalEnabled;
            PublicBaseUrl = LocalPublicBaseUrl;
        }
        else if (!string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            Enabled = false;
        }
    }
}

public sealed class GoAmlSmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string From { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

public interface IGoAmlEmailSender
{
    string? ConfigurationProblem { get; }
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}

public sealed class GoAmlSmtpEmailSender(IOptions<GoAmlSmtpOptions> options) : IGoAmlEmailSender
{
    public string? ConfigurationProblem => string.IsNullOrWhiteSpace(options.Value.Host) ||
        !MailAddress.TryCreate(options.Value.From, out _) || options.Value.Port is < 1 or > 65535 ||
        options.Value.Port == 465 || (!string.IsNullOrWhiteSpace(options.Value.UserName) &&
            (string.IsNullOrWhiteSpace(options.Value.Password) || !options.Value.EnableSsl))
        ? "Configure the approved SMTP sender and credentials on the KCAS server." : null;

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        if (ConfigurationProblem is not null) throw new InvalidOperationException(ConfigurationProblem);
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(settings.UserName)) client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        using var message = new MailMessage(settings.From, recipient, subject, body) { IsBodyHtml = false };
        await client.SendMailAsync(message, cancellationToken);
    }
}

public sealed record GoAmlReminderStatus(string Description, GoAmlReminderDelivery? Latest);

public sealed class GoAmlReminderService(ApplicationDbContext db, IOptions<GoAmlReminderOptions> options,
    IGoAmlEmailSender sender, TimeProvider clock, ILogger<GoAmlReminderService> logger)
{
    private string? ConfigurationProblem
    {
        get
        {
            var settings = options.Value;
            if (!settings.Enabled) return "Daily email reminders are disabled for this KCAS environment. Local development requires the separate LocalEnabled switch.";
            if (!MailAddress.TryCreate(settings.Recipient, out _) || settings.HourLocal is < 0 or > 23 ||
                !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return "Configure the reminder recipient, HTTPS KCAS address and South African send hour.";
            return sender.ConfigurationProblem;
        }
    }

    public async Task<GoAmlReminderStatus> LoadStatusAsync()
    {
        var latest = await db.GoAmlReminderDeliveries.AsNoTracking().OrderByDescending(x => x.ReminderDate).FirstOrDefaultAsync();
        return new(ConfigurationProblem ?? $"Daily email to {options.Value.Recipient} at {options.Value.HourLocal:00}:00 South African time.", latest);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return;
        var problem = ConfigurationProblem;
        if (problem is not null) { logger.LogWarning("goAML daily reminder unavailable: {Problem}", problem); return; }
        var now = clock.GetUtcNow();
        var localNow = now.ToOffset(TimeSpan.FromHours(2));
        if (localNow.Hour < options.Value.HourLocal) return;
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (await db.GoAmlDailyChecks.AnyAsync(x => x.CheckDate == today && x.Status != GoAmlCheckStatuses.Started, cancellationToken)) return;
        var settings = await db.GoAmlSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings is not null && today < settings.TrackingStartDate) return;

        var delivery = await db.GoAmlReminderDeliveries.AsNoTracking().SingleOrDefaultAsync(x => x.ReminderDate == today, cancellationToken);
        if (delivery is null)
        {
            delivery = new GoAmlReminderDelivery { ReminderDate = today, Recipient = options.Value.Recipient.Trim() };
            db.GoAmlReminderDeliveries.Add(delivery);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 })
            {
                db.Entry(delivery).State = EntityState.Detached;
                delivery = await db.GoAmlReminderDeliveries.AsNoTracking().SingleAsync(x => x.ReminderDate == today, cancellationToken);
            }
            db.Entry(delivery).State = EntityState.Detached;
        }
        var utcNow = now.UtcDateTime;
        // A process restart during sending leaves an uncertain result, never an automatic duplicate resend.
        if (delivery.Status == "Sending" && delivery.LastAttemptAtUtc < utcNow.AddMinutes(-10))
        {
            await db.GoAmlReminderDeliveries.Where(x => x.Id == delivery.Id && x.Status == "Sending")
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Uncertain")
                    .SetProperty(x => x.Error, "Sending was interrupted. Check the sender's delivery log before resending."), cancellationToken);
            return;
        }
        var retryBefore = utcNow.AddMinutes(-30);
        var claimed = await db.GoAmlReminderDeliveries.Where(x => x.Id == delivery.Id && x.Attempts < 3 &&
                (x.Status == "Pending" || x.Status == "Failed" && x.LastAttemptAtUtc <= retryBefore))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Sending")
                .SetProperty(x => x.LastAttemptAtUtc, utcNow).SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.Recipient, options.Value.Recipient.Trim()).SetProperty(x => x.Error, (string?)null), cancellationToken);
        if (claimed != 1) return;
        try
        {
            var link = new Uri(new Uri(options.Value.PublicBaseUrl), "/compliance/goaml").AbsoluteUri;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await sender.SendAsync(options.Value.Recipient.Trim(), $"KCAS: goAML access reminder - {today:yyyy-MM-dd}",
                $"Please access the FIC goAML message board today.\n\nOpen KCAS: {link}\n\nSign in and select Open goAML. KCAS records the date, time and user; no screenshot is required.\nRead the message board and record any message requiring follow-up in KCAS.\n\nThis email does not record access or confirm a message-board outcome.", timeout.Token);
            await db.GoAmlReminderDeliveries.Where(x => x.Id == delivery.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Sent")
                    .SetProperty(x => x.SentAtUtc, clock.GetUtcNow().UtcDateTime), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "goAML reminder delivery {DeliveryId} failed.", delivery.Id);
            // Rejected recipients are safe to retry; other SMTP failures can occur after acceptance.
            var rejected = ex is SmtpFailedRecipientException;
            await db.GoAmlReminderDeliveries.Where(x => x.Id == delivery.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, rejected ? "Failed" : "Uncertain")
                    .SetProperty(x => x.Error, rejected ? "The recipient was rejected. KCAS will retry, up to three attempts today." :
                        "Email transmission failed or timed out. Check the sender's delivery log before resending."), cancellationToken);
        }
    }
}

public sealed class GoAmlReminderJob(IServiceScopeFactory scopes, ILogger<GoAmlReminderJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GoAmlReminderService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "goAML email reminder could not be processed."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
