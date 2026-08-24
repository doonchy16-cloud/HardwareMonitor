using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class ProfileRegistrySyncWorkerTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"HardwareMonitor.SyncWorker.Tests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task One_shot_sync_updates_cache_and_sanitized_worker_status()
    {
        var (worker,store) = await CreateAsync(R(1,"Local"),R(2,"Remote"));
        await using (worker)
        {
            var result = await worker.SynchronizeOnceAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ProfileRegistrySyncStatus.Current,result.Status);
            Assert.Equal(2,(await store.LoadAsync(TestContext.Current.CancellationToken)).Revision);
            Assert.Equal(ProfileRegistrySyncStatus.Current,worker.Status.Status);
            Assert.Equal(2,worker.Status.LocalRevision);
            Assert.Null(worker.Status.ErrorCode);
        }
    }

    [Fact]
    public async Task Conflict_is_visible_in_worker_status_without_losing_local_edit()
    {
        var registry=new ProfileRegistryFileStore(Path.Combine(_root,"profiles.json"));
        var metadata=new ProfileRegistrySyncMetadataFileStore(Path.Combine(_root,"profiles.sync.json"));
        Directory.CreateDirectory(_root);
        await registry.SaveAsync(R(4,"Mine"),TestContext.Current.CancellationToken);
        await metadata.SaveAsync(new ProfileRegistrySyncMetadata(4,4,null),TestContext.Current.CancellationToken);
        var worker=new ProfileRegistrySyncWorker(new ProfileRegistrySyncCoordinator(registry,metadata,new FakeAuthority(R(5,"Theirs"))),TimeSpan.FromMinutes(1));
        await using(worker)
        {
            var result=await worker.SynchronizeOnceAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ProfileRegistrySyncStatus.Conflict,result.Status);
            Assert.Equal(ProfileRegistrySyncStatus.Conflict,worker.Status.Status);
            Assert.Equal(5,worker.Status.RemoteRevision);
            Assert.Equal("Mine",Assert.Single((await registry.LoadAsync(TestContext.Current.CancellationToken)).Profiles).DisplayName);
        }
    }

    [Fact]
    public async Task Invalid_interval_is_rejected()
    {
        var (coordinator,_) = await CreateCoordinatorAsync(R(0,"Local"),R(0,"Remote"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new ProfileRegistrySyncWorker(coordinator,TimeSpan.Zero));
    }

    private async Task<(ProfileRegistrySyncWorker Worker,ProfileRegistryFileStore Store)> CreateAsync(ProfileRegistryDocument local,ProfileRegistryDocument remote)
    {
        var (coordinator,store)=await CreateCoordinatorAsync(local,remote);
        return (new ProfileRegistrySyncWorker(coordinator,TimeSpan.FromMinutes(1)),store);
    }

    private async Task<(ProfileRegistrySyncCoordinator Coordinator,ProfileRegistryFileStore Store)> CreateCoordinatorAsync(ProfileRegistryDocument local,ProfileRegistryDocument remote)
    {
        Directory.CreateDirectory(_root);
        var store=new ProfileRegistryFileStore(Path.Combine(_root,"profiles.json"));
        await store.SaveAsync(local,TestContext.Current.CancellationToken);
        var metadata=new ProfileRegistrySyncMetadataFileStore(Path.Combine(_root,"profiles.sync.json"));
        return (new ProfileRegistrySyncCoordinator(store,metadata,new FakeAuthority(remote)),store);
    }

    private static ProfileRegistryDocument R(long revision,string name)=>new(ProfileContract.CurrentSchemaVersion,revision,
        [new MonitoringProfile(Guid.NewGuid(),name,true,ProfileRole.Viewer,[],ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(20)),new ThermalPolicy(80,92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable))]);

    private sealed class FakeAuthority(ProfileRegistryDocument remote):IProfileRegistryAuthorityClient
    {
        public Task<ProfileRegistryDocument> PullAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(remote);}
        public Task<ProfileRegistryDocument> PushAsync(ProfileRegistryDocument registry,long expected,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();if(remote.Revision!=expected)throw new ProfileRegistryRevisionConflictException(remote.Revision);return Task.FromResult(new ProfileRegistryDocument(registry.SchemaVersion,expected+1,registry.Profiles));}
    }

    public ValueTask DisposeAsync(){if(Directory.Exists(_root))Directory.Delete(_root,true);return ValueTask.CompletedTask;}
}
