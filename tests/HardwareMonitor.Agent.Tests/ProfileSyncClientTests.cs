using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class ProfileSyncClientTests
{
    [Fact]
    public async Task SuccessfulSyncReplacesLocalCacheWithAuthoritativeHostScopedRegistry()
    {
        var deviceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var handler = new SyncHandler(HttpStatusCode.OK, $$"""
            {
              "registry_revision": 9,
              "profiles": [
                {
                  "schema_version": "1.0",
                  "profile_id": "{{profileId}}",
                  "name": "Training monitor",
                  "enabled": true,
                  "device_id": "{{deviceId}}",
                  "capabilities": ["PublishHardwareTelemetry", "PublishDevicePresence"],
                  "viewer_scope": "None",
                  "visible_profile_ids": [],
                  "freshness": {"stale_after_seconds": 5.0, "offline_after_seconds": 20.0},
                  "revision": 3
                }
              ]
            }
            """);
        using var http = new HttpClient(handler);
        var repository = new RecordingRepository(new ProfileRegistrySnapshot(2, []));
        var client = new ProfileSyncClient(
            http,
            new Uri("https://bridge.example/"),
            repository,
            _ => ValueTask.FromResult("host-secret"));

        var result = await client.SyncOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.NotNull(repository.Saved);
        Assert.Equal(9, repository.Saved!.Revision);
        var profile = Assert.Single(repository.Saved.Profiles);
        Assert.Equal(profileId, profile.ProfileId);
        Assert.Equal(deviceId, profile.DeviceId);
        Assert.Equal("Training monitor", profile.Name);
        Assert.Equal(3, profile.Revision);
        Assert.Equal(TimeSpan.FromSeconds(5), profile.FreshnessPolicy.StaleAfter);
        Assert.Equal(TimeSpan.FromSeconds(20), profile.FreshnessPolicy.OfflineAfter);
        Assert.Equal("/v2/hardware-monitor/host/profiles", handler.Path);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("host-secret", handler.Authorization.Parameter);
    }

    [Fact]
    public async Task FailedGatewayResponseLeavesExistingCacheUntouchedAndDoesNotThrow()
    {
        var handler = new SyncHandler(HttpStatusCode.ServiceUnavailable, "{}");
        using var http = new HttpClient(handler);
        var existing = new ProfileRegistrySnapshot(7, []);
        var repository = new RecordingRepository(existing);
        var client = new ProfileSyncClient(
            http,
            new Uri("https://bridge.example/"),
            repository,
            _ => ValueTask.FromResult("host-secret"));

        var result = await client.SyncOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Null(repository.Saved);
        Assert.Same(existing, repository.Current);
    }

    [Fact]
    public async Task InvalidRemoteProfileDoesNotOverwriteValidatedLocalCache()
    {
        var handler = new SyncHandler(HttpStatusCode.OK, """
            {
              "registry_revision": 10,
              "profiles": [
                {
                  "schema_version": "1.0",
                  "profile_id": "not-a-guid",
                  "name": "Broken",
                  "enabled": true,
                  "device_id": null,
                  "capabilities": [],
                  "viewer_scope": "None",
                  "visible_profile_ids": [],
                  "freshness": {"stale_after_seconds": 5.0, "offline_after_seconds": 20.0},
                  "revision": 1
                }
              ]
            }
            """);
        using var http = new HttpClient(handler);
        var existing = new ProfileRegistrySnapshot(7, []);
        var repository = new RecordingRepository(existing);
        var client = new ProfileSyncClient(
            http,
            new Uri("https://bridge.example/"),
            repository,
            _ => ValueTask.FromResult("host-secret"));

        var result = await client.SyncOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Null(repository.Saved);
        Assert.Same(existing, repository.Current);
    }

    private sealed class RecordingRepository : IProfileRepository
    {
        public RecordingRepository(ProfileRegistrySnapshot current) => Current = current;

        public ProfileRegistrySnapshot Current { get; private set; }
        public ProfileRegistrySnapshot? Saved { get; private set; }

        public Task<ProfileRepositoryLoadResult> LoadAsync() =>
            Task.FromResult(ProfileRepositoryLoadResult.Loaded(Current));

        public Task SaveAsync(ProfileRegistrySnapshot snapshot)
        {
            Saved = snapshot;
            Current = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class SyncHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public SyncHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public string? Path { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
