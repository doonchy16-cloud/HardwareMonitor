using System.Net;
using System.Text;
using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class ProfileSyncThermalPolicyTests
{
    [Fact]
    public async Task Host_sync_preserves_custom_thermal_thresholds()
    {
        var deviceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var handler = new JsonHandler($$"""
            {
              "registry_revision": 12,
              "profiles": [{
                "schema_version": "1.0",
                "profile_id": "{{profileId}}",
                "name": "Remote thermal policy",
                "enabled": true,
                "device_id": "{{deviceId}}",
                "capabilities": ["PublishHardwareTelemetry"],
                "viewer_scope": "None",
                "visible_profile_ids": [],
                "freshness": {"stale_after_seconds": 5.0, "offline_after_seconds": 20.0},
                "thermal": {"warm_celsius": 76.0, "hot_celsius": 86.0, "critical_celsius": 94.0},
                "revision": 5
              }]
            }
            """);
        using var http = new HttpClient(handler);
        var repository = new RecordingRepository();
        var client = new ProfileSyncClient(
            http,
            new Uri("https://bridge.example/"),
            repository,
            _ => ValueTask.FromResult("host-secret"));

        Assert.True(await client.SyncOnceAsync(TestContext.Current.CancellationToken));
        var profile = Assert.Single(repository.Saved!.Profiles);
        Assert.Equal(76, profile.ThermalThresholdPolicy.WarmCelsius);
        Assert.Equal(86, profile.ThermalThresholdPolicy.HotCelsius);
        Assert.Equal(94, profile.ThermalThresholdPolicy.CriticalCelsius);
    }

    private sealed class RecordingRepository : IProfileRepository
    {
        public ProfileRegistrySnapshot? Saved { get; private set; }

        public Task<ProfileRepositoryLoadResult> LoadAsync() =>
            Task.FromResult(ProfileRepositoryLoadResult.Loaded(ProfileRegistrySnapshot.Empty));

        public Task SaveAsync(ProfileRegistrySnapshot snapshot)
        {
            Saved = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        public JsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}
