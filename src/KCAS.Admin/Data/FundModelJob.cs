using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class FundModelJob(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<FundModelJob> logger) : BackgroundService
{
    private bool recovered;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Next(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Fund model job unavailable"); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
    private async Task Next(CancellationToken stop)
    {
        using var scope = scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync(stop);
        // Interrupted runs retain their immutable input and can be explicitly rerun.
        if (!recovered)
        {
            await db.ModelRuns.Where(x => x.State == "Running")
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, "Interrupted").SetProperty(x => x.Progress, "Worker interrupted; rerun saved inputs."), stop);
            recovered = true;
        }
        var row = await db.ModelRuns.OrderBy(x => x.Id).FirstOrDefaultAsync(x => x.State == "Queued", stop);
        if (row is null) return;
        if (row.CancelRequested) { row.State = "Cancelled"; await db.SaveChangesAsync(stop); return; }
        var claimed = await db.ModelRuns.Where(x => x.Id == row.Id && x.State == "Queued")
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, "Running"), stop);
        if (claimed != 1) return;
        row.State = "Running"; row.Progress = "Starting calculation worker"; await db.SaveChangesAsync(stop);
        try
        {
            var python = configuration["FundModelling:PythonPath"];
            if (string.IsNullOrWhiteSpace(python) || !File.Exists(python)) throw new InvalidOperationException("Fund modelling runtime is not installed. Configure FundModelling:PythonPath.");
            var script = Path.Combine(AppContext.BaseDirectory, "FundModel", "worker.py");
            if (!File.Exists(script)) script = Path.Combine(environment.ContentRootPath, "FundModel", "worker.py");
            var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add(script);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start calculation worker.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var killer = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            await process.StandardInput.WriteAsync(row.InputJson); process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = ReadProgress(process, factory, row.Id, timeout.Token);
            while (!process.HasExited)
            {
                if (await db.ModelRuns.AsNoTracking().Where(x => x.Id == row.Id).Select(x => x.CancelRequested).SingleAsync(stop)) { timeout.Cancel(); row.CancelRequested = true; break; }
                await Task.Delay(500, timeout.Token);
            }
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask; var errors = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(errors);
            using var json = JsonDocument.Parse(output);
            if (json.RootElement.GetProperty("version").GetString() != "markowitz-1") throw new InvalidOperationException("Unexpected worker version.");
            row.ResultJson = output; row.State = "Complete"; row.Progress = "Calculation complete";
        }
        catch (Exception ex)
        {
            row.State = row.CancelRequested ? "Cancelled" : stop.IsCancellationRequested ? "Interrupted" : "Failed";
            row.Progress = ex.Message.Length > 390 ? ex.Message[..390] : ex.Message;
        }
        row.FinishedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }
    private static async Task<string> ReadProgress(Process process, IDbContextFactory<ApplicationDbContext> factory, long id, CancellationToken ct)
    {
        var last = "Calculation failed";
        while (await process.StandardError.ReadLineAsync(ct) is { } line)
        {
            last = line.Length > 390 ? line[..390] : line;
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.ModelRuns.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Progress, last), ct);
        }
        return last;
    }
}
