using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class GatewayProfileRegistryClientTests : IDisposable
{
    private const string Token = "phase7-test-host-token-1234567890";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Phase7", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Pull_uses_host_auth_and_parses_authoritative_registry()
    {
        var handler = new CaptureHandler(Response(HttpStatusCode.OK, Envelope(Registry(7,"Remote"))));
        using var client = new BridgeGatewayProfileRegistryClient(WriteBridgeFiles(), handler);

        var registry = await client.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://bridge.example.test/v2/host/hardware-monitor/registry", handler.Uri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), handler.Authorization);
        Assert.Equal(7, registry.Revision);
        Assert.Equal("Remote", Assert.Single(registry.Profiles).DisplayName);
    }

    [Fact]
    public async Task Push_sends_expected_revision_and_returns_committed_revision()
    {
        var handler = new CaptureHandler(Response(HttpStatusCode.OK, Envelope(Registry(8,"Committed"))));
        using var client = new BridgeGatewayProfileRegistryClient(WriteBridgeFiles(), handler);

        var registry = await client.PushAsync(Registry(7,"Edited"), 7, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Method);
        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        Assert.Equal("hardware-monitor.registry.v1", body.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("expected_revision").GetInt64());
        Assert.Equal(7, body.RootElement.GetProperty("registry").GetProperty("revision").GetInt64());
        Assert.Equal(8, registry.Revision);
    }

    [Fact]
    public async Task Push_conflict_surfaces_remote_revision_without_credentials()
    {
        var handler = new CaptureHandler(Response(HttpStatusCode.Conflict, "{\"error\":\"revision_conflict\",\"current_revision\":9}"));
        using var client = new BridgeGatewayProfileRegistryClient(WriteBridgeFiles(), handler);

        var ex = await Assert.ThrowsAsync<ProfileRegistryRevisionConflictException>(() =>
            client.PushAsync(Registry(7,"Edited"), 7, TestContext.Current.CancellationToken));

        Assert.Equal(9, ex.RemoteRevision);
        Assert.DoesNotContain(Token, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_failure_is_sanitized_authority_unavailable()
    {
        var handler = new CaptureHandler(Response(HttpStatusCode.ServiceUnavailable, $"secret body {Token}"));
        using var client = new BridgeGatewayProfileRegistryClient(WriteBridgeFiles(), handler);

        var ex = await Assert.ThrowsAsync<ProfileRegistryAuthorityUnavailableException>(() =>
            client.PullAsync(TestContext.Current.CancellationToken));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, ex.ToString(), StringComparison.Ordinal);
    }

    private string WriteBridgeFiles()
    {
        var root = Path.Combine(_root,"bridge"); var config = Path.Combine(root,"config"); Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config,"transport-v2.local.json"), JsonSerializer.Serialize(new
        { schema_version="2.0", mode="direct", gateway_url="https://bridge.example.test/", host_token=Token }));
        File.WriteAllText(Path.Combine(config,"identity.json"), JsonSerializer.Serialize(new
        { schema_version="1.1", device_id=Guid.NewGuid().ToString() }));
        return root;
    }

    private static ProfileRegistryDocument Registry(long revision,string name) => new(ProfileContract.CurrentSchemaVersion, revision,
        [new MonitoringProfile(Guid.NewGuid(),name,true,ProfileRole.Viewer,[],ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(20)),new ThermalPolicy(80,92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable))]);

    private static string Envelope(ProfileRegistryDocument registry)
    {
        using var doc=JsonDocument.Parse(ProfileJsonSerializer.Serialize(registry));
        return JsonSerializer.Serialize(new Dictionary<string,object?>
        { ["schema_version"]="hardware-monitor.registry.v1", ["registry"]=doc.RootElement.Clone() });
    }
    private static HttpResponseMessage Response(HttpStatusCode code,string body) => new(code)
    { Content=new StringContent(body,Encoding.UTF8,"application/json") };

    public void Dispose() { if(Directory.Exists(_root)) Directory.Delete(_root,true); }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Method=request.Method; Uri=request.RequestUri; Authorization=request.Headers.Authorization;
            Body=request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return response;
        }
    }
}
