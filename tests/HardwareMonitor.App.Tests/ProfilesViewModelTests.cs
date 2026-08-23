using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.App.Tests;

public sealed class ProfilesViewModelTests
{
    [Fact]
    public async Task First_launch_with_empty_repository_has_zero_profiles_and_add_action()
    {
        var repository = new FakeProfileRepository(ProfileRegistrySnapshot.Empty);
        var viewModel = new ProfilesViewModel(repository);

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.Profiles);
        Assert.True(viewModel.IsEmpty);
        var editor = viewModel.CreateEditor();
        Assert.True(editor.IsNew);
        Assert.Equal(string.Empty, editor.Name);
    }

    [Fact]
    public async Task Create_edit_enable_disable_and_delete_are_persisted_without_built_in_names()
    {
        var repository = new FakeProfileRepository(ProfileRegistrySnapshot.Empty);
        var viewModel = new ProfilesViewModel(repository);
        await viewModel.LoadAsync();

        var editor = viewModel.CreateEditor();
        editor.Name = "My training profile";
        editor.StaleAfterSeconds = 6;
        editor.OfflineAfterSeconds = 24;
        await viewModel.SaveEditorAsync(editor);

        var created = Assert.Single(viewModel.Profiles);
        Assert.Equal("My training profile", created.Name);
        Assert.DoesNotContain(viewModel.Profiles, profile =>
            profile.Name.Contains("Forgey", StringComparison.OrdinalIgnoreCase) ||
            profile.Name.Contains("Main-PC", StringComparison.OrdinalIgnoreCase) ||
            profile.Name.Contains("My Phone", StringComparison.OrdinalIgnoreCase));

        var edit = viewModel.EditProfile(created.ProfileId);
        edit.Name = "Renamed by user";
        await viewModel.SaveEditorAsync(edit);
        Assert.Equal("Renamed by user", Assert.Single(viewModel.Profiles).Name);

        await viewModel.SetEnabledAsync(created.ProfileId, false);
        Assert.False(Assert.Single(viewModel.Profiles).Enabled);

        await viewModel.SetEnabledAsync(created.ProfileId, true);
        Assert.True(Assert.Single(viewModel.Profiles).Enabled);

        await viewModel.DeleteProfileAsync(created.ProfileId);
        Assert.Empty(viewModel.Profiles);
        Assert.True(viewModel.IsEmpty);
        Assert.True(repository.SaveCount >= 5);
    }

    [Fact]
    public async Task Toggle_enabled_preserves_custom_thermal_policy()
    {
        var repository = new FakeProfileRepository(ProfileRegistrySnapshot.Empty);
        var viewModel = new ProfilesViewModel(repository);
        await viewModel.LoadAsync();

        var editor = viewModel.CreateEditor();
        editor.Name = "Custom thermal profile";
        editor.WarmCelsius = 74;
        editor.HotCelsius = 84;
        editor.CriticalCelsius = 91;
        await viewModel.SaveEditorAsync(editor);
        var created = Assert.Single(viewModel.Profiles);

        await viewModel.SetEnabledAsync(created.ProfileId, false);

        var toggled = Assert.Single(viewModel.Profiles);
        Assert.Equal(74, toggled.ThermalThresholdPolicy.WarmCelsius);
        Assert.Equal(84, toggled.ThermalThresholdPolicy.HotCelsius);
        Assert.Equal(91, toggled.ThermalThresholdPolicy.CriticalCelsius);
    }

    [Fact]
    public async Task Capability_toggles_and_viewer_scope_round_trip_through_repository()
    {
        var repository = new FakeProfileRepository(ProfileRegistrySnapshot.Empty);
        var viewModel = new ProfilesViewModel(repository);
        await viewModel.LoadAsync();

        var selectedProfileId = Guid.NewGuid();
        var editor = viewModel.CreateEditor();
        editor.Name = "Viewer configured by user";
        editor.ViewerScope = ViewerScope.SelectedProfiles;
        editor.VisibleProfileIds.Add(selectedProfileId);
        editor.SetCapability(ProfileCapability.ViewProfiles, true);
        editor.SetCapability(ProfileCapability.ReceiveNotifications, true);

        await viewModel.SaveEditorAsync(editor);

        var saved = Assert.Single(repository.Current.Profiles);
        Assert.Equal(ViewerScope.SelectedProfiles, saved.ViewerScope);
        Assert.Contains(selectedProfileId, saved.VisibleProfileIds);
        Assert.Contains(ProfileCapability.ViewProfiles, saved.Capabilities);
        Assert.Contains(ProfileCapability.ReceiveNotifications, saved.Capabilities);

        var edit = viewModel.EditProfile(saved.ProfileId);
        edit.SetCapability(ProfileCapability.ReceiveNotifications, false);
        edit.ViewerScope = ViewerScope.AllProfiles;
        edit.VisibleProfileIds.Clear();
        await viewModel.SaveEditorAsync(edit);

        var updated = Assert.Single(repository.Current.Profiles);
        Assert.Equal(ViewerScope.AllProfiles, updated.ViewerScope);
        Assert.DoesNotContain(ProfileCapability.ReceiveNotifications, updated.Capabilities);
        Assert.Contains(ProfileCapability.ViewProfiles, updated.Capabilities);
    }

    [Fact]
    public async Task Invalid_stale_offline_values_are_rejected_by_FreshnessPolicy_before_save()
    {
        var repository = new FakeProfileRepository(ProfileRegistrySnapshot.Empty);
        var viewModel = new ProfilesViewModel(repository);
        await viewModel.LoadAsync();

        var editor = viewModel.CreateEditor();
        editor.Name = "Invalid until corrected";
        editor.StaleAfterSeconds = 20;
        editor.OfflineAfterSeconds = 10;

        Assert.False(editor.TryBuildProfile(out _));
        Assert.Contains("offline", editor.ValidationError!, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SaveEditorAsync(editor));
        Assert.Empty(repository.Current.Profiles);

        editor.StaleAfterSeconds = 5;
        editor.OfflineAfterSeconds = 20;
        Assert.True(editor.TryBuildProfile(out var valid));
        Assert.NotNull(valid);
        Assert.Null(editor.ValidationError);
    }

    private sealed class FakeProfileRepository : IProfileRepository
    {
        public FakeProfileRepository(ProfileRegistrySnapshot current)
        {
            Current = current;
        }

        public ProfileRegistrySnapshot Current { get; private set; }
        public int SaveCount { get; private set; }

        public Task<ProfileRepositoryLoadResult> LoadAsync() =>
            Task.FromResult(ProfileRepositoryLoadResult.Loaded(Current));

        public Task SaveAsync(ProfileRegistrySnapshot snapshot)
        {
            Current = snapshot;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
