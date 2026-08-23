using System.Security.Cryptography;
using System.Text;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors;
using TheSpark.HardwareMonitor.Sensors.Agent;

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
AgentRuntimeOptions options;
try
{
    options = AgentRuntimeOptions.Parse(args, localAppData);
}
catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
{
    return 2;
}

var mutexName = BuildMutexName(options.PipeName);
using var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
if (!createdNew)
{
    return 0;
}

var monitorService = new HardwareMonitorService(new LibreHardwareMonitorProvider(), options.PollInterval);
await using var agent = new BackgroundHardwareAgent(
    monitorService,
    new ProfileRegistryFileStore(options.ProfilePath));
await using var ipcServer = new AgentIpcServer(agent, options.PipeName);

await agent.StartAsync().ConfigureAwait(false);
await ipcServer.StartAsync().ConfigureAwait(false);

using var shutdown = new CancellationTokenSource();
void ProcessExit(object? sender, EventArgs eventArgs) => shutdown.Cancel();
AppDomain.CurrentDomain.ProcessExit += ProcessExit;

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
finally
{
    AppDomain.CurrentDomain.ProcessExit -= ProcessExit;
}

return 0;

static string BuildMutexName(string pipeName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pipeName));
    return $"Local\\TheSpark.HardwareMonitor.Agent.{Convert.ToHexString(hash)}";
}
