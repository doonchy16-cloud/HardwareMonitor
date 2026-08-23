using System.Net;
using System.Net.Http.Headers;
using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Remote;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class RemoteTelemetryPublisherTests
{
    [Fact]
    public async Task NewerFrameForSameProfileReplacesOlderFrameInsteadOfGrowingQueue()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var publisher = new RemoteTelemetryPublisher(
            client,
            new Uri("https://bridge.example/"),
            _ => ValueTask.FromResult("viewer-safe-host-secret"));
        var profileId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        publisher.QueueTelemetry(Telemetry(deviceId, profileId, 71));
        publisher.QueueTelemetry(Telemetry(deviceId, profileId, 79));

        Assert.Equal(1, publisher.PendingTelemetryCount);
        Assert.True(await publisher.FlushOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, publisher.PendingTelemetryCount);
        Assert.Single(handler.Bodies);
        Assert.Contains("\"numeric_value\":79", handler.Bodies[0]);
        Assert.DoesNotContain("viewer-safe-host-secret", handler.Bodies[0]);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("viewer-safe-host-secret", handler.Authorization.Parameter);
    }

    [Fact]
    public async Task GatewayFailureKeepsLatestFramePendingAndDoesNotThrow()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        var publisher = new RemoteTelemetryPublisher(
            client,
            new Uri("https://bridge.example/"),
            _ => ValueTask.FromResult("host-secret"));

        publisher.QueueTelemetry(Telemetry(Guid.NewGuid(), Guid.NewGuid(), 88));

        var first = await publisher.FlushOnceAsync(TestContext.Current.CancellationToken);
        Assert.False(first);
        Assert.Equal(1, publisher.PendingTelemetryCount);

        handler.StatusCode = HttpStatusCode.OK;
        var second = await publisher.FlushOnceAsync(TestContext.Current.CancellationToken);
        Assert.True(second);
        Assert.Equal(0, publisher.PendingTelemetryCount);
    }

    [Fact]
    public async Task PresenceUsesDedicatedEndpointAndIsAlsoLatestStateOnly()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var publisher = new RemoteTelemetryPublisher(
            client,
            new Uri("https://bridge.example/"),
            _ => ValueTask.FromResult("host-secret"));
        var deviceId = Guid.NewGuid();

        publisher.QueuePresence(new PresenceEnvelope(deviceId, DateTimeOffset.UtcNow, "Windows", "1.0.0", "STALE"));
        publisher.QueuePresence(new PresenceEnvelope(deviceId, DateTimeOffset.UtcNow, "Windows", "1.0.0", "ONLINE"));

        Assert.True(await publisher.FlushOnceAsync(TestContext.Current.CancellationToken));
        Assert.Contains(handler.Paths, path => path == "/v2/hardware-monitor/presence");
        Assert.Contains(handler.Bodies, body => body.Contains("\"state\":\"ONLINE\"", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Bodies, body => body.Contains("\"state\":\"STALE\"", StringComparison.Ordinal));
    }

    private static TelemetryEnvelope Telemetry(Guid deviceId, Guid profileId, double temperature) =>
        new(
            deviceId,
            profileId,
            DateTimeOffset.UtcNow,
            "Training",
            "Healthy",
            [new TelemetryMetricEnvelope("gpu.temperature", "GPU", temperature, null, "°C", "Available")]);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public RecordingHandler(HttpStatusCode statusCode) => StatusCode = statusCode;

        public HttpStatusCode StatusCode { get; set; }
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            Authorization = request.Headers.Authorization;
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(StatusCode);
        }
    }
}
