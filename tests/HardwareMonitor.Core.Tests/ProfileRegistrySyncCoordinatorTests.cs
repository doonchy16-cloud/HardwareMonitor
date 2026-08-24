using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistrySyncCoordinatorTests
{
    [Fact]
    public async Task Pulls_newer_remote_into_cache()
    {
        await using var f = await Fixture.CreateAsync(R(2,"Local"), R(5,"Remote"));
        var x = await f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.Current, x.Status);
        Assert.Equal(5, x.Registry.Revision);
        Assert.Equal("Remote", Assert.Single(x.Registry.Profiles).DisplayName);
    }

    [Fact]
    public async Task Offline_keeps_cache_and_reports_stale()
    {
        await using var f = await Fixture.CreateAsync(R(3,"Cached"), R(3,"Remote"));
        f.Authority.Unavailable = true;
        var x = await f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.Stale, x.Status);
        Assert.Equal("Cached", Assert.Single(x.Registry.Profiles).DisplayName);
    }

    [Fact]
    public async Task Offline_edit_is_persistent_pending_change()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"), R(4,"Before"));
        f.Authority.Unavailable = true;
        var x = await f.Coordinator.SaveLocalMutationAsync(R(4,"Offline edit"), TestContext.Current.CancellationToken);
        var m = await f.MetadataStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.PendingUpload, x.Status);
        Assert.Equal(4, m.PendingBaseRevision);
        Assert.Equal("Offline edit", Assert.Single((await f.RegistryStore.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
    }

    [Fact]
    public async Task Reconnect_commits_pending_once()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"), R(4,"Before"));
        f.Authority.Unavailable = true;
        await f.Coordinator.SaveLocalMutationAsync(R(4,"Offline edit"), TestContext.Current.CancellationToken);
        f.Authority.Unavailable = false;
        var x = await f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
        var m = await f.MetadataStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.Current, x.Status);
        Assert.Equal(5, x.Registry.Revision);
        Assert.Equal("Offline edit", Assert.Single(x.Registry.Profiles).DisplayName);
        Assert.Null(m.PendingBaseRevision);
        Assert.Equal(4, Assert.Single(f.Authority.Expected));
    }

    [Fact]
    public async Task Conflict_preserves_pending_local_edit()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"), R(4,"Before"));
        f.Authority.Unavailable = true;
        await f.Coordinator.SaveLocalMutationAsync(R(4,"Mine"), TestContext.Current.CancellationToken);
        f.Authority.Unavailable = false;
        f.Authority.Remote = R(5,"Theirs");
        var x = await f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
        var m = await f.MetadataStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.Conflict, x.Status);
        Assert.Equal("Mine", Assert.Single((await f.RegistryStore.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
        Assert.Equal(4, m.PendingBaseRevision);
        Assert.Equal(5, x.RemoteRevision);
        Assert.Equal(ProfileRegistrySyncStatus.Conflict, m.LastStatus);
        Assert.Equal(5, m.LastObservedRemoteRevision);
        Assert.Equal(nameof(ProfileRegistryRevisionConflictException), m.LastErrorCode);
    }

    [Fact]
    public async Task Revision_zero_local_bootstraps_empty_authority()
    {
        await using var f = await Fixture.CreateAsync(R(0,"Existing"), ProfileRegistryDocument.Empty);
        var x = await f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ProfileRegistrySyncStatus.Current, x.Status);
        Assert.Equal(1, x.Registry.Revision);
        Assert.Equal("Existing", Assert.Single(x.Registry.Profiles).DisplayName);
        Assert.Equal(0, Assert.Single(f.Authority.Expected));
    }

    private static ProfileRegistryDocument R(long rev, string name) => new(
        ProfileContract.CurrentSchemaVersion, rev,
        [new MonitoringProfile(Guid.NewGuid(), name, true, ProfileRole.Viewer, [], ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)), new ThermalPolicy(80,92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable))]);

    private sealed class FakeAuthority(ProfileRegistryDocument remote) : IProfileRegistryAuthorityClient
    {
        public ProfileRegistryDocument Remote { get; set; } = remote;
        public bool Unavailable { get; set; }
        public List<long> Expected { get; } = [];
        public Task<ProfileRegistryDocument> PullAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Unavailable) throw new ProfileRegistryAuthorityUnavailableException("offline");
            return Task.FromResult(Remote);
        }
        public Task<ProfileRegistryDocument> PushAsync(ProfileRegistryDocument registry, long expectedRevision, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Unavailable) throw new ProfileRegistryAuthorityUnavailableException("offline");
            Expected.Add(expectedRevision);
            if (Remote.Revision != expectedRevision) throw new ProfileRegistryRevisionConflictException(Remote.Revision);
            Remote = new ProfileRegistryDocument(registry.SchemaVersion, checked(expectedRevision + 1), registry.Profiles);
            return Task.FromResult(Remote);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string root, ProfileRegistryFileStore registryStore, ProfileRegistrySyncMetadataFileStore metadataStore, FakeAuthority authority)
        {
            Root=root; RegistryStore=registryStore; MetadataStore=metadataStore; Authority=authority;
            Coordinator=new ProfileRegistrySyncCoordinator(registryStore, metadataStore, authority);
        }
        public string Root { get; }
        public ProfileRegistryFileStore RegistryStore { get; }
        public ProfileRegistrySyncMetadataFileStore MetadataStore { get; }
        public FakeAuthority Authority { get; }
        public ProfileRegistrySyncCoordinator Coordinator { get; }
        public static async Task<Fixture> CreateAsync(ProfileRegistryDocument local, ProfileRegistryDocument remote)
        {
            var root=Path.Combine(Path.GetTempPath(),"HardwareMonitor.Sync.Tests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var registry=new ProfileRegistryFileStore(Path.Combine(root,"profiles.json"));
            var metadata=new ProfileRegistrySyncMetadataFileStore(Path.Combine(root,"profiles.sync.json"));
            await registry.SaveAsync(local, TestContext.Current.CancellationToken);
            return new Fixture(root,registry,metadata,new FakeAuthority(remote));
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root,true); return ValueTask.CompletedTask; }
    }
}
