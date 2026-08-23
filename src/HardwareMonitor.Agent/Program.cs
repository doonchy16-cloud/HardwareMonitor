using TheSpark.HardwareMonitor.Sensors;

namespace TheSpark.HardwareMonitor.Agent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        await using var host = new AgentHost(new LibreHardwareMonitorProvider(), TimeSpan.FromSeconds(1));
        await using var ipc = new LocalIpcServer(host);
        using var runtime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);

        try
        {
            var monitoringTask = host.RunAsync(runtime.Token);
            var ipcTask = ipc.RunAsync(runtime.Token);
            var first = await Task.WhenAny(monitoringTask, ipcTask).ConfigureAwait(false);

            if (!runtime.IsCancellationRequested && first.IsCompleted)
            {
                await first.ConfigureAwait(false);
                runtime.Cancel();
            }

            try
            {
                await Task.WhenAll(monitoringTask, ipcTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runtime.IsCancellationRequested)
            {
            }

            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            runtime.Cancel();
            Console.Error.WriteLine($"Hardware Monitor agent failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
