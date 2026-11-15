using System.Diagnostics;

namespace KCAS.Admin.Data;

public sealed class FundModelRuntime(IConfiguration configuration, IHostEnvironment environment)
{
    public async Task<string> CheckAsync(CancellationToken ct=default)
    {
        var python=configuration["FundModelling:PythonPath"];
        if(string.IsNullOrWhiteSpace(python)||!File.Exists(python))return "Not configured: saved results can be viewed and transferred, but new calculations require FundModelling:PythonPath.";
        if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"FundModel","worker.py"))&&!File.Exists(Path.Combine(environment.ContentRootPath,"FundModel","worker.py")))return "Calculation worker is missing from this deployment. Saved results remain viewable.";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try {
            var start=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("-c");start.ArgumentList.Add("import numpy, scipy; from scipy.optimize import minimize; print('KCAS-RUNTIME-OK')");
            using var process=Process.Start(start)??throw new InvalidOperationException("Could not start Python.");
            using var kill=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);var text=await output;await error;
            return process.ExitCode==0&&text.Contains("KCAS-RUNTIME-OK",StringComparison.Ordinal)?"Ready: Python, NumPy, SciPy and the calculation worker are available.":"Runtime check failed: check Python and NumPy/SciPy installation for the KCAS service account. Saved results remain viewable.";
        }catch(Exception ex) when(ex is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException) {
            return "Runtime check failed or timed out. Saved results can still be imported and viewed; configure the runtime before new calculations.";
        }
    }
}
