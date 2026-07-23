using System.Collections.Concurrent;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceScanCoordinator(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> runningScans = new();

    public async Task<int> StartScanAsync(string? requestedRootPath, string? userName, string reason)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var runId = await service.StartScanRunAsync(requestedRootPath, userName, reason);
        var cancellation = new CancellationTokenSource();
        if (!runningScans.TryAdd(runId, cancellation))
        {
            cancellation.Dispose();
            throw new InvalidOperationException("Unable to track the evidence scan.");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var runScope = scopeFactory.CreateScope();
                var runService = runScope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
                await runService.ExecuteScanRunAsync(runId, userName, reason, cancellation.Token);
            }
            finally
            {
                if (runningScans.TryRemove(runId, out var completedCancellation))
                {
                    completedCancellation.Dispose();
                }
            }
        });

        return runId;
    }

    public async Task CancelScanAsync(int runId, string? userName, string reason)
    {
        if (runningScans.TryGetValue(runId, out var cancellation))
        {
            cancellation.Cancel();
            using var cancellationScope = scopeFactory.CreateScope();
            var cancellationService = cancellationScope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
            await cancellationService.RequestScanCancellationAsync(runId, userName, reason);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        await service.CancelUntrackedScanAsync(runId, userName, reason);
    }
}
