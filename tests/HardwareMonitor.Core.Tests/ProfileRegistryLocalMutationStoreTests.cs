using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryLocalMutationStoreTests
{
    [Fact]
    public async Task Save_marks_first_edit_pending_at_cached_authoritative_revision()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"));
        var edited = R(4,"Edited");

        var metadata = await f.Mutations.SaveAsync(edited, TestContext.Current.CancellationToken);

        Assert.Equal(4, metadata.PendingBaseRevision);
        Assert.Equal("Edited", Assert.Single((await f.Registry.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
    }

    [Fact]
    public async Task Additional_offline_edits_keep_original_pending_base_revision()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"));
        await f.Mutations.SaveAsync(R(4,"First"), TestContext.Current.CancellationToken);

        var metadata = await f.Mutations.SaveAsync(R(4,"Second"), TestContext.Current.CancellationToken);

        Assert.Equal(4, metadata.PendingBaseRevision);
        Assert.Equal("Second", Assert.Single((await f.Registry.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
    }

    [Fact]
    public async Task Save_rejects_revision_mismatch_without_touching_cache()
    {
        await using var f = await Fixture.CreateAsync(R(4,"Before"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Mutations.SaveAsync(R(3,"Stale edit"), TestContext.Current.CancellationToken));

        Assert.Equal("Before", Assert.Single((await f.Registry.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
        Assert.Null((await f.Metadata.LoadAsync(TestContext.Current.CancellationToken)).PendingBaseRevision);
    }

    private static ProfileRegistryDocument R(long revision,string name) => new(ProfileContract.CurrentSchemaVersion,revision,
        [new MonitoringProfile(Guid.NewGuid(),name,true,ProfileRole.Viewer,[],ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(20)),new ThermalPolicy(80,92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable))]);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string root, ProfileRegistryFileStore registry, ProfileRegistrySyncMetadataFileStore metadata)
        { Root=root;Registry=registry;Metadata=metadata;Mutations=new ProfileRegistryLocalMutationStore(registry,metadata); }
        public string Root { get; }
        public ProfileRegistryFileStore Registry { get; }
        public ProfileRegistrySyncMetadataFileStore Metadata { get; }
        public ProfileRegistryLocalMutationStore Mutations { get; }
        public static async Task<Fixture> CreateAsync(ProfileRegistryDocument initial)
        {
            var root=Path.Combine(Path.GetTempPath(),"HardwareMonitor.Mutation.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var registry=new ProfileRegistryFileStore(Path.Combine(root,"profiles.json"));
            var metadata=new ProfileRegistrySyncMetadataFileStore(Path.Combine(root,"profiles.sync.json"));
            await registry.SaveAsync(initial,TestContext.Current.CancellationToken);
            return new Fixture(root,registry,metadata);
        }
        public ValueTask DisposeAsync(){Directory.Delete(Root,true);return ValueTask.CompletedTask;}
    }
}
