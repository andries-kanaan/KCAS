using Microsoft.Extensions.Options;

namespace KCAS.Admin.Data;

public sealed class SanctionsAutomationJob(IServiceScopeFactory scopes, IOptions<SanctionsAutomationOptions> options,
    ILogger<SanctionsAutomationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && options.Value.Enabled)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<SanctionsAutomationService>().RunAsync(stoppingToken);
                if (result is not null) logger.LogInformation("Official sanctions check {Id}: {Outcome}; {Checked} screened; {Review} need review.",
                    result.Id, result.Outcome, result.SubjectsChecked, result.NeedsReview);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Sanctions automation failed. No screening success is inferred."); }
            try { await Task.Delay(TimeSpan.FromMinutes(options.Value.PollIntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
