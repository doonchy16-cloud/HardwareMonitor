using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.App.Pages;

public partial class ProfilesPage : UserControl
{
    private readonly ProfileRegistryFileStore _store;
    private ProfileRegistryDocument _registry = ProfileRegistryDocument.Empty;
    private Guid? _editingProfileId;
    private bool _loadingEditor;
    private bool _loadFailed;
    private bool _loaded;

    public ProfilesPage()
    {
        InitializeComponent();

        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "The Spark",
            "Hardware Monitor",
            "profiles.json");

        _store = new ProfileRegistryFileStore(profilePath);
        Loaded += ProfilesPage_Loaded;
        PrepareNewProfile();
    }

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadRegistryAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadRegistryAsync();

    private async Task ReloadRegistryAsync()
    {
        try
        {
            var loaded = await _store.LoadAsync();
            _registry = loaded;
            _loadFailed = false;
            SetEditingEnabled(true);
            RefreshProfileLists();
            PrepareNewProfile();
            StatusText.Text = $"Loaded {_registry.Profiles.Count} profile(s).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _loadFailed = true;
            SetEditingEnabled(false);
            StatusText.Text = $"Profiles could not be loaded ({ex.GetType().Name}: {ex.Message}). The file was not changed. Repair it, then choose Reload.";
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed)
        {
            return;
        }

        ProfileList.SelectedItem = null;
        PrepareNewProfile();
        StatusText.Text = "New profile draft. Nothing is saved yet.";
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || ProfileList.SelectedItem is not MonitoringProfile profile)
        {
            return;
        }

        LoadProfileIntoEditor(profile);
    }

    private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedScopePanel is null)
        {
            return;
        }

        SelectedScopePanel.Visibility = ScopeCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed)
        {
            return;
        }

        try
        {
            var profile = BuildProfileFromEditor();
            var updated = ProfileRegistryEditor.Upsert(_registry, profile);
            await _store.SaveAsync(updated);

            _registry = updated;
            _editingProfileId = profile.Id;
            RefreshProfileLists();
            SelectProfile(profile.Id);
            StatusText.Text = $"Saved '{profile.DisplayName}'.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StatusText.Text = $"Profile was not saved: {ex.Message}";
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed || !_editingProfileId.HasValue)
        {
            return;
        }

        var existing = _registry.Profiles.FirstOrDefault(profile => profile.Id == _editingProfileId.Value);
        if (existing is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"Delete profile '{existing.DisplayName}'?",
            "Hardware Monitor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var updated = ProfileRegistryEditor.Remove(_registry, existing.Id);
            await _store.SaveAsync(updated);

            _registry = updated;
            RefreshProfileLists();
            PrepareNewProfile();
            StatusText.Text = $"Deleted '{existing.DisplayName}'.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StatusText.Text = $"Profile was not deleted: {ex.Message}";
        }
    }

    private MonitoringProfile BuildProfileFromEditor()
    {
        var roles = ProfileRole.None;
        if (ViewerRoleCheckBox.IsChecked == true)
        {
            roles |= ProfileRole.Viewer;
        }
        if (PublisherRoleCheckBox.IsChecked == true)
        {
            roles |= ProfileRole.Publisher;
        }
        if (TrainingRoleCheckBox.IsChecked == true)
        {
            roles |= ProfileRole.TrainingMonitor;
        }

        var bindings = DeviceBindingsTextBox.Text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static deviceId => new DeviceBinding(deviceId))
            .ToArray();

        ViewerScope viewerScope;
        if (ScopeCombo.SelectedIndex == 1)
        {
            var selectedIds = SelectedScopeProfilesList.SelectedItems
                .OfType<MonitoringProfile>()
                .Select(static profile => profile.Id)
                .ToArray();
            viewerScope = ViewerScope.SelectedProfiles(selectedIds);
        }
        else
        {
            viewerScope = ViewerScope.AllProfiles();
        }

        var staleSeconds = ParsePositiveNumber(StaleSecondsTextBox.Text, "Stale after");
        var offlineSeconds = ParsePositiveNumber(OfflineSecondsTextBox.Text, "Offline after");
        var warningCelsius = ParsePositiveNumber(WarningTemperatureTextBox.Text, "Warning temperature");
        var criticalCelsius = ParsePositiveNumber(CriticalTemperatureTextBox.Text, "Critical temperature");
        var unavailableBehavior = UnavailableSensorsCombo.SelectedIndex == 1
            ? UnavailableSensorBehavior.ShowUnavailable
            : UnavailableSensorBehavior.Hide;

        return new MonitoringProfile(
            _editingProfileId ?? Guid.NewGuid(),
            ProfileNameTextBox.Text,
            EnabledCheckBox.IsChecked == true,
            roles,
            bindings,
            viewerScope,
            new FreshnessPolicy(TimeSpan.FromSeconds(staleSeconds), TimeSpan.FromSeconds(offlineSeconds)),
            new ThermalPolicy(warningCelsius, criticalCelsius),
            new SensorVisibilityPolicy(unavailableBehavior));
    }

    private void PrepareNewProfile()
    {
        _loadingEditor = true;
        try
        {
            _editingProfileId = Guid.NewGuid();
            ProfileNameTextBox.Text = string.Empty;
            EnabledCheckBox.IsChecked = true;
            ViewerRoleCheckBox.IsChecked = true;
            PublisherRoleCheckBox.IsChecked = false;
            TrainingRoleCheckBox.IsChecked = false;
            DeviceBindingsTextBox.Text = string.Empty;
            ScopeCombo.SelectedIndex = 0;
            StaleSecondsTextBox.Text = "10";
            OfflineSecondsTextBox.Text = "30";
            WarningTemperatureTextBox.Text = "80";
            CriticalTemperatureTextBox.Text = "90";
            UnavailableSensorsCombo.SelectedIndex = 0;
            RefreshSelectedScopeSource();
            SelectedScopeProfilesList.SelectedItems.Clear();
            DeleteProfileButton.IsEnabled = false;
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private void LoadProfileIntoEditor(MonitoringProfile profile)
    {
        _loadingEditor = true;
        try
        {
            _editingProfileId = profile.Id;
            ProfileNameTextBox.Text = profile.DisplayName;
            EnabledCheckBox.IsChecked = profile.Enabled;
            ViewerRoleCheckBox.IsChecked = profile.Roles.HasFlag(ProfileRole.Viewer);
            PublisherRoleCheckBox.IsChecked = profile.Roles.HasFlag(ProfileRole.Publisher);
            TrainingRoleCheckBox.IsChecked = profile.Roles.HasFlag(ProfileRole.TrainingMonitor);
            DeviceBindingsTextBox.Text = string.Join(Environment.NewLine, profile.DeviceBindings.Select(static binding => binding.DeviceId));
            ScopeCombo.SelectedIndex = profile.ViewerScope.Mode == ViewerScopeMode.AllProfiles ? 0 : 1;
            StaleSecondsTextBox.Text = profile.Freshness.StaleAfter.TotalSeconds.ToString(CultureInfo.CurrentCulture);
            OfflineSecondsTextBox.Text = profile.Freshness.OfflineAfter.TotalSeconds.ToString(CultureInfo.CurrentCulture);
            WarningTemperatureTextBox.Text = profile.Thermal.WarningCelsius.ToString(CultureInfo.CurrentCulture);
            CriticalTemperatureTextBox.Text = profile.Thermal.CriticalCelsius.ToString(CultureInfo.CurrentCulture);
            UnavailableSensorsCombo.SelectedIndex = profile.SensorVisibility.UnavailableSensors == UnavailableSensorBehavior.ShowUnavailable ? 1 : 0;

            RefreshSelectedScopeSource();
            SelectedScopeProfilesList.SelectedItems.Clear();
            foreach (var scopedId in profile.ViewerScope.ProfileIds)
            {
                var target = SelectedScopeProfilesList.Items.OfType<MonitoringProfile>().FirstOrDefault(candidate => candidate.Id == scopedId);
                if (target is not null)
                {
                    SelectedScopeProfilesList.SelectedItems.Add(target);
                }
            }

            DeleteProfileButton.IsEnabled = true;
            StatusText.Text = $"Editing '{profile.DisplayName}'.";
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private void RefreshProfileLists()
    {
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = _registry.Profiles;
        RefreshSelectedScopeSource();
        RegistrySummaryText.Text = _registry.Profiles.Count == 0
            ? "No profiles saved yet."
            : $"{_registry.Profiles.Count} saved profile(s) · schema v{_registry.SchemaVersion}";
    }

    private void RefreshSelectedScopeSource()
    {
        if (SelectedScopeProfilesList is null)
        {
            return;
        }

        var currentId = _editingProfileId;
        SelectedScopeProfilesList.ItemsSource = null;
        SelectedScopeProfilesList.ItemsSource = _registry.Profiles.Where(profile => profile.Id != currentId).ToArray();
    }

    private void SelectProfile(Guid profileId)
    {
        var profile = _registry.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is not null)
        {
            ProfileList.SelectedItem = profile;
        }
    }

    private void SetEditingEnabled(bool enabled)
    {
        EditorRoot.IsEnabled = enabled;
        ProfileList.IsEnabled = enabled;
        NewProfileButton.IsEnabled = enabled;
    }

    private static double ParsePositiveNumber(string text, string fieldName)
    {
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
             && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            || !double.IsFinite(value)
            || value <= 0)
        {
            throw new ArgumentException($"{fieldName} must be a positive number.");
        }

        return value;
    }
}
