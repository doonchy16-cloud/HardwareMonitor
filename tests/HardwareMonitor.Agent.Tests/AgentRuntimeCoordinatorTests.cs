using System.Net;
using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Alerts;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class AgentRuntimeCoordinatorTests
{
    [Fact]
    public async Task One_physical_snapshot_routes_to_every_enabled_bound_publisher_profile_and_presence_once()
    {
        var deviceId = Guid.NewGuid();
        var first = Profile(deviceId, "First", training: false);
        var second = Profile(deviceId, "Second", training: true);
        var ignored = Profile(Guid.NewGuid(), "Other device", training: false);
        var repository = new StaticRepository(new ProfileRegistrySnapshot(3, [first, second, ignored]));
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var publisher = new RemoteTelemetryPublisher(
            http,
            new Uri("https://bridge.example/"),
            _ => ValueTask.FromResult("host-secret"));
        var coordinator = new AgentRuntimeCoordinator(
            deviceId,
            repository,
            publisher,
            new AlertEngine(),
            platform: "Windows",
            agentVersion: "1.0.0");
        var now = DateTimeOffset.Parse("2026-08-23T08:10:00Z");

        await coordinator.ProcessSnapshotAsync(Snapshot(now, 77), TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, handler.Requests.Count(item => item.Path == "/v2/hardware-monitor/telemetry"));
        Assert.Equal(1, handler.Requests.Count(item => item.Path == "/v2/hardware-monitor/presence"));
        Assert.Contains(handler.Requests, item => item.Body.Contains(first.ProfileId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, item => item.Body.Contains(second.ProfileId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(handler.Requests, item => item.Body.Contains(ignored.ProfileId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, item => item.Body.Contains("Training", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_or_nonpublishing_profiles_never_emit_hardware_telemetry()
    {
        var deviceId = Guid.NewGuid();
        var enabled = Profile(deviceId, "Enabled", training: false);
        var disabled = new HardwareProfile(
            Guid.NewGuid(), "Disabled", deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
            ViewerScope.None, new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            enabled: false, revision: 1);
        var viewerOnly = new HardwareProfile(
            Guid.NewGuid(), "Viewer", deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.ViewProfiles },
            ViewerScope.AllProfiles, new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            revision: 1);
        var repository = new StaticRepository(new ProfileRegistrySnapshot(4, [enabled, disabled, viewerOnly]));
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var coordinator = new AgentRuntimeCoordinator(
            deviceId,
            repository,
            new RemoteTelemetryPublisher(http, new Uri("https://bridge.example/"), _ => ValueTask.FromResult("host-secret")),
            new AlertEngine(),
            "Windows", "1.0.0");

        await coordinator.ProcessSnapshotAsync(
            Snapshot(DateTimeOffset.Parse("2026-08-23T08:10:00Z"), 66),
            TestContext.Current.CancellationToken);

        var telemetry = handler.Requests.Where(item => item.Path == "/v2/hardware-monitor/telemetry").ToArray();
        Assert.Single(telemetry);
        Assert.Contains(enabled.ProfileId.ToString(), telemetry[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(disabled.ProfileId.ToString(), telemetry[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(viewerOnly.ProfileId.ToString(), telemetry[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gateway_failure_does_not_throw_and_latest_state_remains_pending_for_retry()
    {
        var deviceId = Guid.NewGuid();
        var profile = Profile(deviceId, "Training", training: true);
        var repository = new StaticRepository(new ProfileRegistrySnapshot(1, [profile]));
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var publisher = new RemoteTelemetryPublisher(
            http,
            new Uri("https://bridge.example/"),
            _ => ValueTask.FromResult("host-secret"));
        var coordinator = new AgentRuntimeCoordinator(
            deviceId, repository, publisher, new AlertEngine(), "Windows", "1.0.0");

        var result = await coordinator.ProcessSnapshotAsync(
            Snapshot(DateTimeOffset.Parse("2026-08-23T08:10:00Z"), 88),
            TestContext.Current.CancellationToken);

        Assert.False(result.RemoteFlushSucceeded);
        Assert.Equal(1, publisher.PendingTelemetryCount);
        Assert.Equal(1, publisher.PendingPresenceCount);
        Assert.Single(result.AlertEvents);
        Assert.Equal(AlertKind.ThermalHot, result.AlertEvents[0].Kind);
    }

    [Fact]
    public async Task Unavailable_sensor_rows_are_not_published_or_used_for_alerts()
    {
        var deviceId = Guid.NewGuid();
        var profile = Profile(deviceId, "Safe", training: true);
        var repository = new StaticRepository(new ProfileRegistrySnapshot(1, [profile]));
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var coordinator = new AgentRuntimeCoordinator(
            deviceId,
            repository,
            new RemoteTelemetryPublisher(http, new Uri("https://bridge.example/"), _ => ValueTask.FromResult("host-secret")),
            new AlertEngine(),
            "Windows", "1.0.0");
        var now = DateTimeOffset.Parse("2026-08-23T08:10:00Z");
        var snapshot = new HardwareSnapshot(
            now,
            [new HardwareDeviceSnapshot(
                "gpu0", "GPU", HardwareKind.Gpu,
                [
                    new SensorReading("valid", "GPU Temp", SensorKind.Temperature, 60, "°C", now, SensorAvailability.Available),
                    new SensorReading("fake", "Unavailable", SensorKind.Temperature, 120, "°C", now, SensorAvailability.NotExposed)
                ])],
            "Healthy");

        var result = await coordinator.ProcessSnapshotAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(result.AlertEvents);
        var telemetry = Assert.Single(handler.Requests, item => item.Path == "/v2/hardware-monitor/telemetry");
        Assert.Contains("valid", telemetry.Body);
        Assert.DoesNotContain("fake", telemetry.Body);
    }

    private static HardwareProfile Profile(Guid deviceId, string name, bool training) =>
        new(
            Guid.NewGuid(), name, deviceId,
            new HashSet<ProfileCapability>(training
                ? [ProfileCapability.PublishHardwareTelemetry, ProfileCapability.PublishDevicePresence, ProfileCapability.TrainingMode]
                : [ProfileCapability.PublishHardwareTelemetry, ProfileCapability.PublishDevicePresence]),
            ViewerScope.None,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            revision: 1,
            thermalThresholdPolicy: new ThermalThresholdPolicy(70, 80, 90));

    private static HardwareSnapshot Snapshot(DateTimeOffset at, double gpuTemp) =>
        new(
            at,
            [new HardwareDeviceSnapshot(
                "gpu0", "GPU", HardwareKind.Gpu,
                [new SensorReading("gpu-temp", "GPU Temp", SensorKind.Temperature, gpuTemp, "°C", at, SensorAvailability.Available)])],
            "Healthy");

    private sealed class StaticRepository : IProfileRepository
    {
        private readonly ProfileRegistrySnapshot _snapshot;
        public StaticRepository(ProfileRegistrySnapshot snapshot) => _snapshot = snapshot;
        public Task<ProfileRepositoryLoadResult> LoadAsync() => Task.FromResult(ProfileRepositoryLoadResult.Loaded(_snapshot));
        public Task SaveAsync(ProfileRegistrySnapshot snapshot) => throw new NotSupportedException();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public RecordingHandler(HttpStatusCode statusCode) => _statusCode = statusCode;
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, body));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{}")
            };
        }
    }
}
