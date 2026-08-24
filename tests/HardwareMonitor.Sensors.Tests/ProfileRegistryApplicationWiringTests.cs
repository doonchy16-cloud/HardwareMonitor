using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class ProfileRegistryApplicationWiringTests
{
    [Fact]
    public void Agent_program_owns_authenticated_registry_sync_worker()
    {
        var root=FindRepositoryRoot();
        var program=File.ReadAllText(Path.Combine(root,"src","HardwareMonitor.Agent","Program.cs"));

        Assert.Contains("BridgeGatewayProfileRegistryClient",program,StringComparison.Ordinal);
        Assert.Contains("ProfileRegistrySyncMetadataFileStore",program,StringComparison.Ordinal);
        Assert.Contains("ProfileRegistrySyncCoordinator",program,StringComparison.Ordinal);
        Assert.Contains("ProfileRegistrySyncWorker",program,StringComparison.Ordinal);
        Assert.Contains("options.ProfileSyncMetadataPath",program,StringComparison.Ordinal);
        Assert.Contains("options.ProfileSyncInterval",program,StringComparison.Ordinal);
        Assert.Contains("profileSyncWorker.StartAsync",program,StringComparison.Ordinal);
        Assert.Contains("profileSyncWorker.DisposeAsync",program,StringComparison.Ordinal);
    }

    [Fact]
    public void Profiles_page_marks_mutations_pending_without_bridge_credentials()
    {
        var root=FindRepositoryRoot();
        var page=File.ReadAllText(Path.Combine(root,"src","HardwareMonitor.App","Pages","ProfilesPage.xaml.cs"));

        Assert.Contains("ProfileRegistrySyncMetadataFileStore",page,StringComparison.Ordinal);
        Assert.Contains("ProfileRegistryLocalMutationStore",page,StringComparison.Ordinal);
        Assert.Contains("profiles.sync.json",page,StringComparison.Ordinal);
        Assert.Contains("_mutationStore.SaveAsync(updated)",page,StringComparison.Ordinal);
        Assert.Contains("ProfileRegistrySyncStatus.Conflict",page,StringComparison.Ordinal);
        Assert.DoesNotContain("BridgeGatewayProfileRegistryClient",page,StringComparison.Ordinal);
        Assert.DoesNotContain("host_token",page,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_store.SaveAsync(updated)",page,StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        while(current is not null)
        {
            if(File.Exists(Path.Combine(current.FullName,"HardwareMonitor.sln"))) return current.FullName;
            current=current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate HardwareMonitor.sln from test output path.");
    }
}
