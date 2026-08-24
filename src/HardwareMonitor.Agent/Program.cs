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

var profileStore = new ProfileRegistryFileStore(options.ProfilePath);
var monitorService = new HardwareMonitorService(new LibreHardwareMonitorProvider(), options.PollInterval);
await using var agent = new BackgroundHardwareAgent(monitorService, profileStore);
await using var ipcServer = new AgentIpcServer(agent, options.PipeName);

BridgeGatewayTelemetryPublisher? telemetryPublisher = null;
BridgeGatewayProfileRegistryClient? profileRegistryClient = null;
ProfileRegistrySyncWorker? profileSyncWorker = null;
if (options.BridgeRoot is not null)
{
    telemetryPublisher = new BridgeGatewayTelemetryPublisher(
        options.BridgeRoot,
        options.TelemetrySequencePath,
        profileStore,
        new HttpClientHandler());
    monitorService.SnapshotUpdated += telemetryPublisher.Queue;

    try
    {
        profileRegistryClient = new BridgeGatewayProfileRegistryClient(
            options.BridgeRoot,
            new HttpClientHandler());
        var profileSyncMetadataStore = new ProfileRegistrySyncMetadataFileStore(options.ProfileSyncMetadataPath);
        var profileSyncCoordinator = new ProfileRegistrySyncCoordinator(
            profileStore,
            profileSyncMetadataStore,
            profileRegistryClient);
        profileSyncWorker = new ProfileRegistrySyncWorker(profileSyncCoordinator, options.ProfileSyncInterval);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        profileRegistryClient?.Dispose();
        profileRegistryClient = null;
        profileSyncWorker = null;
    }
}

using var shutdown = new CancellationTokenSource();
void ProcessExit(object? sender, EventArgs eventArgs) => shutdown.Cancel();
AppDomain.CurrentDomain.ProcessExit += ProcessExit;

try
{
    if (profileSyncWorker is not null)
    {
        await profileSyncWorker.StartAsync().ConfigureAwait(false);
    }

    if (telemetryPublisher is not null)
    {
        await telemetryPublisher.StartAsync().ConfigureAwait(false);
    }

    await agent.StartAsync().ConfigureAwait(false);
    await ipcServer.StartAsync().ConfigureAwait(false);

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
    }
}
finally
{
    AppDomain.CurrentDomain.ProcessExit -= ProcessExit;
    if (profileSyncWorker is not null)
    {
        await profileSyncWorker.DisposeAsync().ConfigureAwait(false);
    }
    profileRegistryClient?.Dispose();

    if (telemetryPublisher is not null)
    {
        monitorService.SnapshotUpdated -= telemetryPublisher.Queue;
        await telemetryPublisher.DisposeAsync().ConfigureAwait(false);
    }
}

return 0;

static string BuildMutexName(string pipeName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pipeName));
    return $"Local\\TheSpark.HardwareMonitor.Agent.{Convert.ToHexString(hash)}";
}
