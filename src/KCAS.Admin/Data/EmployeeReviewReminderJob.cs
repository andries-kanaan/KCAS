namespace KCAS.Admin.Data;

public sealed class EmployeeReviewReminderJob(IServiceScopeFactory scopes, ILogger<EmployeeReviewReminderJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<EmployeeComplianceService>().RefreshDueTasksForJobAsync();
                if (count > 0) logger.LogInformation("Created {Count} in-app employee review tasks.", count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Employee review reminders could not be refreshed; no screening success is inferred."); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
