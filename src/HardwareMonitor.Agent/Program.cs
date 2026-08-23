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
        try
        {
            await host.RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Hardware Monitor agent failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
