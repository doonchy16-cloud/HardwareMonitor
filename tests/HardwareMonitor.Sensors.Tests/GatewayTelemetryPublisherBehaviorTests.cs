using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class GatewayTelemetryPublisherBehaviorTests : IDisposable
{
    private const string TestToken = "phase6b-test-host-token-1234567890";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Phase6B", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Bridge_root_is_explicit_opt_in_and_sequence_state_is_local_and_durable()
    {
        var localAppData = Path.Combine(_root, "local-app-data");
        var bridgeRoot = Path.Combine(_root, "bridge");

        var defaults = AgentRuntimeOptions.Parse(Array.Empty<string>(), localAppData);
        Assert.Null(defaults.BridgeRoot);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(localAppData, "The Spark", "Hardware Monitor", "gateway-telemetry-sequence.json")),
            defaults.TelemetrySequencePath);

        var explicitOptions = AgentRuntimeOptions.Parse(["--bridge-root", bridgeRoot], localAppData);
        Assert.Equal(Path.GetFullPath(bridgeRoot), explicitOptions.BridgeRoot);
        Assert.Equal(defaults.TelemetrySequencePath, explicitOptions.TelemetrySequencePath);

        Assert.ThrowsAny<ArgumentException>(() => AgentRuntimeOptions.Parse(["--bridge-root", "   "], localAppData));
    }

    [Fact]
    public async Task Valid_bridge_config_enables_publisher_but_empty_registry_never_touches_network_or_sequence()
    {
        var deviceId = Guid.NewGuid().ToString();
        var bridgeRoot = WriteBridgeFiles(deviceId);
        var sequencePath = Path.Combine(_root, "state", "sequence.json");
        var profileStore = new ProfileRegistryFileStore(Path.Combine(_root, "profiles.json"));
        var handler = new CaptureHandler();
        await using var publisher = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, handler);

        Assert.True(publisher.Status.Enabled);
        Assert.DoesNotContain(TestToken, publisher.Status.ToString(), StringComparison.Ordinal);

        var published = await publisher.PublishAsync(HardwareSnapshot.Empty(DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        Assert.False(published);
        Assert.Equal(0, handler.CallCount);
        Assert.False(File.Exists(sequencePath));
    }

    [Fact]
    public async Task One_shot_publish_uses_existing_host_identity_and_exact_gateway_schema_without_registry_metadata()
    {
        var deviceId = Guid.NewGuid().ToString();
        var bridgeRoot = WriteBridgeFiles(deviceId);
        var sequencePath = Path.Combine(_root, "state", "sequence.json");
        var profileStore = await WritePublisherProfileAsync(deviceId);
        var handler = new CaptureHandler();
        await using var publisher = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, handler);

        var published = await publisher.PublishAsync(CreateSnapshot(), TestContext.Current.CancellationToken);

        Assert.True(published);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://bridge.example.test/v2/host/hardware-monitor/telemetry", handler.LastUri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", TestToken), handler.LastAuthorization);

        using var json = JsonDocument.Parse(Assert.IsType<string>(handler.LastBody));
        var root = json.RootElement;
        Assert.Equal(
            ["device_id", "profiles", "schema_version", "sent_at", "sequence"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("hardware-monitor.telemetry.v1", root.GetProperty("schema_version").GetString());
        Assert.Equal(deviceId, root.GetProperty("device_id").GetString());
        Assert.Equal(1, root.GetProperty("sequence").GetInt64());

        var profile = root.GetProperty("profiles")[0];
        Assert.Equal(
            ["captured_at", "devices", "engine_status", "freshness", "profile_id", "source_device_id"],
            profile.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(deviceId, profile.GetProperty("source_device_id").GetString());
        Assert.False(profile.TryGetProperty("display_name", out _));
        Assert.False(profile.TryGetProperty("roles", out _));
        Assert.Equal(5000, profile.GetProperty("freshness").GetProperty("stale_after_ms").GetInt64());
        Assert.Equal(20000, profile.GetProperty("freshness").GetProperty("offline_after_ms").GetInt64());

        var sensor = profile.GetProperty("devices")[0].GetProperty("sensors")[0];
        Assert.Equal("Temperature", sensor.GetProperty("kind").GetString());
        Assert.Equal("Available", sensor.GetProperty("availability").GetString());
        Assert.Equal("Normal", sensor.GetProperty("thermal_state").GetString());
        Assert.Equal(61.5, sensor.GetProperty("value").GetDouble());

        Assert.Equal(1, publisher.Status.LastAcceptedSequence);
        Assert.Equal(1, publisher.Status.LastProfileCount);
        Assert.Equal(1, publisher.Status.LastSensorCount);
        Assert.NotNull(publisher.Status.LastSuccessAt);
        Assert.Null(publisher.Status.LastErrorCode);
        Assert.DoesNotContain(TestToken, publisher.Status.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(sequencePath));
    }

    [Fact]
    public async Task Sequence_is_durable_and_failed_delivery_consumes_ambiguous_sequence_before_retry()
    {
        var deviceId = Guid.NewGuid().ToString();
        var bridgeRoot = WriteBridgeFiles(deviceId);
        var sequencePath = Path.Combine(_root, "state", "sequence.json");
        var profileStore = await WritePublisherProfileAsync(deviceId);

        var failingHandler = new CaptureHandler(throwOnSend: true);
        await using (var first = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, failingHandler))
        {
            var firstPublished = await first.PublishAsync(CreateSnapshot(), TestContext.Current.CancellationToken);
            Assert.False(firstPublished);
            Assert.Equal(1, failingHandler.Sequences.Single());
            Assert.Equal(nameof(HttpRequestException), first.Status.LastErrorCode);
            Assert.DoesNotContain(TestToken, first.Status.ToString(), StringComparison.Ordinal);
        }

        var succeedingHandler = new CaptureHandler();
        await using (var second = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, succeedingHandler))
        {
            var secondPublished = await second.PublishAsync(CreateSnapshot(), TestContext.Current.CancellationToken);
            Assert.True(secondPublished);
            Assert.Equal(2, succeedingHandler.Sequences.Single());
            Assert.Equal(2, second.Status.LastAcceptedSequence);
        }
    }

    [Fact]
    public async Task Http_rejection_reports_sanitized_status_code_without_response_body_or_credentials()
    {
        var deviceId = Guid.NewGuid().ToString();
        var bridgeRoot = WriteBridgeFiles(deviceId);
        var sequencePath = Path.Combine(_root, "state", "sequence.json");
        var profileStore = await WritePublisherProfileAsync(deviceId);
        var handler = new CaptureHandler(responseStatusCode: HttpStatusCode.BadRequest);
        await using var publisher = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, handler);

        var published = await publisher.PublishAsync(CreateSnapshot(), TestContext.Current.CancellationToken);

        Assert.False(published);
        Assert.Equal("HttpStatus400", publisher.Status.LastErrorCode);
        Assert.DoesNotContain(TestToken, publisher.Status.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("simulated private gateway rejection", publisher.Status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_and_queue_publish_in_background_without_blocking_sensor_event_caller()
    {
        var deviceId = Guid.NewGuid().ToString();
        var bridgeRoot = WriteBridgeFiles(deviceId);
        var sequencePath = Path.Combine(_root, "state", "sequence.json");
        var profileStore = await WritePublisherProfileAsync(deviceId);
        var handler = new CaptureHandler();
        await using var publisher = new BridgeGatewayTelemetryPublisher(bridgeRoot, sequencePath, profileStore, handler);

        await publisher.StartAsync(TestContext.Current.CancellationToken);
        publisher.Queue(CreateSnapshot());

        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Standalone_agent_program_wires_publisher_only_through_explicit_bridge_root()
    {
        var repoRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "HardwareMonitor.Agent", "Program.cs"));

        Assert.Contains("options.BridgeRoot", program, StringComparison.Ordinal);
        Assert.Contains("BridgeGatewayTelemetryPublisher", program, StringComparison.Ordinal);
        Assert.Contains("SnapshotUpdated", program, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\ChatGPT-Terminal-Bridge-Global", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\ChatGPT-Terminal-Bridge-Global", program, StringComparison.OrdinalIgnoreCase);
    }

    private string WriteBridgeFiles(string deviceId)
    {
        var bridgeRoot = Path.Combine(_root, "bridge");
        var config = Path.Combine(bridgeRoot, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "transport-v2.local.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = "2.0",
                mode = "direct",
                gateway_url = "https://bridge.example.test",
                host_token = TestToken,
            }));
        File.WriteAllText(
            Path.Combine(config, "identity.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = "1.1",
                device_id = deviceId,
            }));
        return bridgeRoot;
    }

    private async Task<ProfileRegistryFileStore> WritePublisherProfileAsync(string deviceId)
    {
        var store = new ProfileRegistryFileStore(Path.Combine(_root, "profiles.json"));
        var profile = new MonitoringProfile(
            Guid.NewGuid(),
            "Local publisher profile",
            true,
            ProfileRole.Publisher,
            [new DeviceBinding(deviceId)],
            ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            new ThermalPolicy(80, 95),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.Hide));
        await store.SaveAsync(
            new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [profile]),
            TestContext.Current.CancellationToken);
        return store;
    }

    private static HardwareSnapshot CreateSnapshot()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);
        return new HardwareSnapshot(
            capturedAt,
            [new HardwareDeviceSnapshot(
                "cpu-0",
                "CPU",
                HardwareKind.Cpu,
                [new SensorReading(
                    "cpu-temp-package",
                    "Package",
                    SensorKind.Temperature,
                    61.5,
                    "C",
                    capturedAt,
                    SensorAvailability.Available)])],
            "Healthy");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HardwareMonitor.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate HardwareMonitor.sln from test output path.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CaptureHandler(
        bool throwOnSend = false,
        HttpStatusCode responseStatusCode = HttpStatusCode.Accepted) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }
        public List<long> Sequences { get; } = [];
        public TaskCompletionSource<bool> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(LastBody))
            {
                using var json = JsonDocument.Parse(LastBody);
                if (json.RootElement.TryGetProperty("sequence", out var sequence))
                {
                    Sequences.Add(sequence.GetInt64());
                }
            }

            Received.TrySetResult(true);
            if (throwOnSend)
            {
                throw new HttpRequestException("simulated network failure");
            }

            var acceptedSequence = Sequences.Count == 0 ? 0 : Sequences[^1];
            var responseBody = responseStatusCode == HttpStatusCode.Accepted
                ? JsonSerializer.Serialize(new { ok = true, sequence = acceptedSequence })
                : "simulated private gateway rejection";
            return new HttpResponseMessage(responseStatusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
