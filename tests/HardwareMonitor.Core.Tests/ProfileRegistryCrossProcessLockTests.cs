using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryCrossProcessLockTests
{
    [Fact]
    public async Task Independent_lock_instances_serialize_same_registry_path()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json.operation.lock");
            var first = new ProfileRegistryOperationLock(path);
            var second = new ProfileRegistryOperationLock(path);
            var firstLease = await first.AcquireAsync(TestContext.Current.CancellationToken);
            try
            {
                var secondTask = second.AcquireAsync(TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.False(secondTask.IsCompleted);
                await firstLease.DisposeAsync();
                firstLease = null!;
                await using var secondLease = await secondTask;
            }
            finally
            {
                if (firstLease is not null) await firstLease.DisposeAsync();
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Local_mutation_waits_for_shared_registry_operation_lock()
    {
        await using var f = await Fixture.CreateAsync();
        var gate = new ProfileRegistryOperationLock(f.Registry.FilePath + ".operation.lock");
        var lease = await gate.AcquireAsync(TestContext.Current.CancellationToken);
        try
        {
            var edited = Registry(0, "Edited");
            var saveTask = f.Mutations.SaveAsync(edited, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(saveTask.IsCompleted);
            await lease.DisposeAsync();
            lease = null!;
            var metadata = await saveTask;
            Assert.Equal(0, metadata.PendingBaseRevision);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task Agent_sync_waits_for_shared_registry_operation_lock_before_cache_mutation()
    {
        await using var f = await Fixture.CreateAsync();
        var gate = new ProfileRegistryOperationLock(f.Registry.FilePath + ".operation.lock");
        var lease = await gate.AcquireAsync(TestContext.Current.CancellationToken);
        try
        {
            f.Authority.Remote = Registry(1, "Remote");
            var syncTask = f.Coordinator.SynchronizeAsync(TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(syncTask.IsCompleted);
            await lease.DisposeAsync();
            lease = null!;
            var result = await syncTask;
            Assert.Equal(ProfileRegistrySyncStatus.Current, result.Status);
            Assert.Equal(1, result.Registry.Revision);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    private static ProfileRegistryDocument Registry(long revision, string name) => new(
        ProfileContract.CurrentSchemaVersion, revision,
        [new MonitoringProfile(Guid.NewGuid(), name, true, ProfileRole.Viewer, [], ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)), new ThermalPolicy(80, 92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable))]);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HardwareMonitor.CrossProcess.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeAuthority : IProfileRegistryAuthorityClient
    {
        public ProfileRegistryDocument Remote { get; set; } = ProfileRegistryDocument.Empty;
        public Task<ProfileRegistryDocument> PullAsync(CancellationToken ct) => Task.FromResult(Remote);
        public Task<ProfileRegistryDocument> PushAsync(ProfileRegistryDocument registry, long expectedRevision, CancellationToken ct)
        {
            Remote = new ProfileRegistryDocument(registry.SchemaVersion, expectedRevision + 1, registry.Profiles);
            return Task.FromResult(Remote);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string root, ProfileRegistryFileStore registry, ProfileRegistrySyncMetadataFileStore metadata, FakeAuthority authority)
        {
            Root = root; Registry = registry; Metadata = metadata; Authority = authority;
            Mutations = new ProfileRegistryLocalMutationStore(registry, metadata);
            Coordinator = new ProfileRegistrySyncCoordinator(registry, metadata, authority);
        }
        public string Root { get; }
        public ProfileRegistryFileStore Registry { get; }
        public ProfileRegistrySyncMetadataFileStore Metadata { get; }
        public FakeAuthority Authority { get; }
        public ProfileRegistryLocalMutationStore Mutations { get; }
        public ProfileRegistrySyncCoordinator Coordinator { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var root = CreateTempDirectory();
            var registry = new ProfileRegistryFileStore(Path.Combine(root, "profiles.json"));
            var metadata = new ProfileRegistrySyncMetadataFileStore(Path.Combine(root, "profiles.sync.json"));
            await registry.SaveAsync(Registry(0, "Local"), TestContext.Current.CancellationToken);
            return new Fixture(root, registry, metadata, new FakeAuthority());
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
