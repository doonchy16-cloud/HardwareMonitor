using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.App.Pages;

public partial class ProfilesPage : UserControl
{
    private readonly ProfilesViewModel _viewModel;
    private ProfileEditorViewModel? _editor;

    public ProfilesPage(ProfilesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        ViewerScopeBox.ItemsSource = Enum.GetValues<ViewerScope>();
        Loaded += ProfilesPage_Loaded;
        ViewerScopeBox.SelectionChanged += ViewerScopeBox_SelectionChanged;
    }

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProfilesPage_Loaded;
        try
        {
            await _viewModel.LoadAsync();
            RefreshPageState();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorText.Text = _viewModel.ErrorMessage ?? ex.Message;
            RefreshPageState();
        }
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) => OpenEditor(_viewModel.CreateEditor());

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid profileId })
        {
            OpenEditor(_viewModel.EditProfile(profileId));
        }
    }

    private async void ToggleProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid profileId })
        {
            return;
        }

        var profile = _viewModel.Profiles.FirstOrDefault(item => item.ProfileId == profileId);
        if (profile is null)
        {
            return;
        }

        await RunMutationAsync(() => _viewModel.SetEnabledAsync(profileId, !profile.Enabled));
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid profileId })
        {
            return;
        }

        var profile = _viewModel.Profiles.FirstOrDefault(item => item.ProfileId == profileId);
        if (profile is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Delete profile ‘{profile.Name}’? This removes the profile configuration from this device.",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunMutationAsync(() => _viewModel.DeleteProfileAsync(profileId));
    }

    private void CancelEditor_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var editor = _editor;
        if (editor is null)
        {
            return;
        }

        ValidationText.Text = string.Empty;
        editor.Name = NameBox.Text;
        editor.Enabled = EnabledCheck.IsChecked == true;
        editor.ViewerScope = ViewerScopeBox.SelectedItem is ViewerScope scope ? scope : ViewerScope.None;

        if (!TryParseOptionalGuid(DeviceIdBox.Text, out var deviceId, out var deviceError))
        {
            ValidationText.Text = deviceError;
            return;
        }
        editor.DeviceId = deviceId;

        if (!double.TryParse(StaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var staleSeconds) ||
            !double.TryParse(OfflineBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var offlineSeconds))
        {
            ValidationText.Text = "Stale and offline thresholds must be numbers.";
            return;
        }
        editor.StaleAfterSeconds = staleSeconds;
        editor.OfflineAfterSeconds = offlineSeconds;

        if (!double.TryParse(WarmBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var warmCelsius) ||
            !double.TryParse(HotBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hotCelsius) ||
            !double.TryParse(CriticalBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var criticalCelsius))
        {
            ValidationText.Text = "Warm, Hot, and Critical thermal thresholds must be numbers.";
            return;
        }
        editor.WarmCelsius = warmCelsius;
        editor.HotCelsius = hotCelsius;
        editor.CriticalCelsius = criticalCelsius;

        ApplyCapabilities(editor);
        ApplySelectedProfiles(editor);

        if (!editor.TryBuildProfile(out _))
        {
            ValidationText.Text = editor.ValidationError ?? "Profile configuration is invalid.";
            return;
        }

        try
        {
            await _viewModel.SaveEditorAsync(editor);
            CloseEditor();
            RefreshPageState();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ValidationText.Text = ex.Message;
        }
    }

    private void ViewerScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshSelectedProfilesVisibility();

    private void OpenEditor(ProfileEditorViewModel editor)
    {
        _editor = editor;
        EditorTitle.Text = editor.IsNew ? "Add Profile" : "Edit Profile";
        NameBox.Text = editor.Name;
        DeviceIdBox.Text = editor.DeviceId?.ToString() ?? string.Empty;
        EnabledCheck.IsChecked = editor.Enabled;
        ViewerScopeBox.SelectedItem = editor.ViewerScope;
        StaleBox.Text = editor.StaleAfterSeconds.ToString(CultureInfo.InvariantCulture);
        OfflineBox.Text = editor.OfflineAfterSeconds.ToString(CultureInfo.InvariantCulture);
        WarmBox.Text = editor.WarmCelsius.ToString(CultureInfo.InvariantCulture);
        HotBox.Text = editor.HotCelsius.ToString(CultureInfo.InvariantCulture);
        CriticalBox.Text = editor.CriticalCelsius.ToString(CultureInfo.InvariantCulture);
        ValidationText.Text = string.Empty;

        SetCapabilityChecks(editor);
        PopulateVisibleProfiles(editor);
        RefreshSelectedProfilesVisibility();

        EditorPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void CloseEditor()
    {
        _editor = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        ValidationText.Text = string.Empty;
    }

    private void PopulateVisibleProfiles(ProfileEditorViewModel editor)
    {
        VisibleProfilesList.Items.Clear();
        foreach (var profile in _viewModel.Profiles.Where(item => item.ProfileId != editor.ProfileId))
        {
            var item = new ListBoxItem
            {
                Content = profile.Name,
                Tag = profile.ProfileId
            };
            VisibleProfilesList.Items.Add(item);
            if (editor.VisibleProfileIds.Contains(profile.ProfileId))
            {
                VisibleProfilesList.SelectedItems.Add(item);
            }
        }
    }

    private void ApplySelectedProfiles(ProfileEditorViewModel editor)
    {
        editor.VisibleProfileIds.Clear();
        if (editor.ViewerScope != ViewerScope.SelectedProfiles)
        {
            return;
        }

        foreach (var item in VisibleProfilesList.SelectedItems.OfType<ListBoxItem>())
        {
            if (item.Tag is Guid profileId)
            {
                editor.VisibleProfileIds.Add(profileId);
            }
        }
    }

    private void RefreshSelectedProfilesVisibility()
    {
        var scope = ViewerScopeBox.SelectedItem is ViewerScope value ? value : ViewerScope.None;
        SelectedProfilesPanel.Visibility = scope == ViewerScope.SelectedProfiles
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetCapabilityChecks(ProfileEditorViewModel editor)
    {
        ViewProfilesCheck.IsChecked = editor.HasCapability(ProfileCapability.ViewProfiles);
        PublishHardwareCheck.IsChecked = editor.HasCapability(ProfileCapability.PublishHardwareTelemetry);
        PublishPresenceCheck.IsChecked = editor.HasCapability(ProfileCapability.PublishDevicePresence);
        LimitedTelemetryCheck.IsChecked = editor.HasCapability(ProfileCapability.PublishLimitedClientTelemetry);
        TrainingModeCheck.IsChecked = editor.HasCapability(ProfileCapability.TrainingMode);
        ManageProfilesCheck.IsChecked = editor.HasCapability(ProfileCapability.ManageProfiles);
        ManageDevicesCheck.IsChecked = editor.HasCapability(ProfileCapability.ManageDevices);
        ManageAlertsCheck.IsChecked = editor.HasCapability(ProfileCapability.ManageAlerts);
        ManageRemoteAccessCheck.IsChecked = editor.HasCapability(ProfileCapability.ManageRemoteAccess);
        NotificationsCheck.IsChecked = editor.HasCapability(ProfileCapability.ReceiveNotifications);
        DiagnosticsCheck.IsChecked = editor.HasCapability(ProfileCapability.ViewDiagnostics);
        HistoryCheck.IsChecked = editor.HasCapability(ProfileCapability.ViewHistory);
    }

    private void ApplyCapabilities(ProfileEditorViewModel editor)
    {
        editor.SetCapability(ProfileCapability.ViewProfiles, ViewProfilesCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.PublishHardwareTelemetry, PublishHardwareCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.PublishDevicePresence, PublishPresenceCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.PublishLimitedClientTelemetry, LimitedTelemetryCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.TrainingMode, TrainingModeCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ManageProfiles, ManageProfilesCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ManageDevices, ManageDevicesCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ManageAlerts, ManageAlertsCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ManageRemoteAccess, ManageRemoteAccessCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ReceiveNotifications, NotificationsCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ViewDiagnostics, DiagnosticsCheck.IsChecked == true);
        editor.SetCapability(ProfileCapability.ViewHistory, HistoryCheck.IsChecked == true);
    }

    private async Task RunMutationAsync(Func<Task> mutation)
    {
        try
        {
            await mutation();
            ErrorText.Text = string.Empty;
            RefreshPageState();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void RefreshPageState()
    {
        EmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(ErrorText.Text))
        {
            ErrorText.Text = _viewModel.ErrorMessage ?? string.Empty;
        }
    }

    private static bool TryParseOptionalGuid(string? text, out Guid? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = null;
            return true;
        }

        if (Guid.TryParse(text.Trim(), out var parsed) && parsed != Guid.Empty)
        {
            value = parsed;
            error = null;
            return true;
        }

        value = null;
        error = "Device ID must be a valid non-empty GUID, or left blank.";
        return false;
    }
}
