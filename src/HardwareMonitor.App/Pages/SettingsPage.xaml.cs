using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TheSpark.HardwareMonitor.App.Services;
using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Diagnostics;
using TheSpark.HardwareMonitor.Sensors;

namespace TheSpark.HardwareMonitor.App.Pages;

public partial class SettingsPage : UserControl
{
    private const string PackageName = "TheSpark.HardwareMonitor";
    private const string ExecutionAlias = "HardwareMonitor.exe";

    private readonly HardwareMonitorService _monitorService;
    private readonly TelemetryViewModel _telemetry;
    private readonly RotatingDiagnosticLog _log;
    private readonly AppSettings _settings;
    private bool _initializing = true;

    public SettingsPage(HardwareMonitorService monitorService, TelemetryViewModel telemetry, RotatingDiagnosticLog log, AppSettings settings)
    {
        InitializeComponent();
        _monitorService = monitorService;
        _telemetry = telemetry;
        _log = log;
        _settings = settings;
        DataContext = telemetry;
        TemperatureUnitCombo.SelectedIndex = settings.TemperatureUnit.Equals("Fahrenheit", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        MotionCombo.SelectedIndex = settings.Motion.Equals("Reduced", StringComparison.OrdinalIgnoreCase) ? 1 : settings.Motion.Equals("Off", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        PollCombo.SelectedIndex = settings.PollIntervalMilliseconds <= 500 ? 0 : settings.PollIntervalMilliseconds >= 2000 ? 2 : 1;
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"} · MSIX 1.0.0.0";
        _initializing = false;
    }

    public event Action<string>? ThemeChanged;
    public event Action<string>? TemperatureUnitChanged;

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string theme }) return;
        _settings.Theme = theme;
        ThemeManager.Apply(theme);
        ThemeChanged?.Invoke(theme);
        SaveSettings();
    }

    private void MotionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || MotionCombo.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        _settings.Motion = value;
        MotionPreferences.Level = value;
        SaveSettings();
    }

    private void PollCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || PollCombo.SelectedItem is not ComboBoxItem { Tag: string value } || !int.TryParse(value, out var milliseconds)) return;
        _settings.PollIntervalMilliseconds = milliseconds;
        _monitorService.PollInterval = TimeSpan.FromMilliseconds(milliseconds);
        SaveSettings();
    }

    private void TemperatureUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || TemperatureUnitCombo.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        _settings.TemperatureUnit = value;
        TemperatureUnitChanged?.Invoke(value);
        SaveSettings();
    }

    private async void RestartEngine_Click(object sender, RoutedEventArgs e)
    {
        await _log.WriteAsync("INFO", "Sensor engine restart requested.");
        await _monitorService.RestartAsync();
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var report = new StringBuilder().AppendLine("Hardware Monitor diagnostic report").AppendLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}").AppendLine("Channel: stable").AppendLine($"Engine: {DiagnosticSanitizer.SanitizeValue(_telemetry.EngineStatus)}").AppendLine($"Devices: {_telemetry.DeviceCount}").AppendLine($"Sensors: {_telemetry.SensorCount}").AppendLine($"Last refresh: {_telemetry.LastRefresh:O}").AppendLine($"Log: {_log.CurrentLogPath}").ToString();
        Clipboard.SetText(report);
    }

    private void OpenStartupApps_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:startupapps");

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (!IsInstalledPackageAvailable())
        {
            ShowInstalledBuildRequired("Repair");
            return;
        }

        var script = "$package = Get-AppxPackage -Name '" + PackageName + "' | Select-Object -First 1; " +
                     "if ($null -ne $package) { " +
                     "$uri = 'ms-settings:appsfeatures-app?' + [Uri]::EscapeDataString($package.PackageFamilyName); " +
                     "Start-Process $uri }";

        if (!StartDetachedPowerShell(script))
        {
            MessageBox.Show("Windows could not open Hardware Monitor's repair controls.", "Hardware Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var aliasPath = GetExecutionAliasPath();
        if (!File.Exists(aliasPath))
        {
            ShowInstalledBuildRequired("Automatic updates");
            return;
        }

        var script = $"Start-Sleep -Milliseconds 900; Start-Process -FilePath '{PowerShellQuote(aliasPath)}'";
        if (!StartDetachedPowerShell(script))
        {
            MessageBox.Show("Windows could not restart Hardware Monitor to check for updates.", "Hardware Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Application.Current.Shutdown();
    }

    private static bool IsInstalledPackageAvailable() => File.Exists(GetExecutionAliasPath());

    private static string GetExecutionAliasPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "WindowsApps",
        ExecutionAlias);

    private static bool StartDetachedPowerShell(string script)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(
                "powershell.exe",
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string PowerShellQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void ShowInstalledBuildRequired(string action) => MessageBox.Show(
        $"{action} is available after Hardware Monitor is installed through the Windows installer.",
        "Hardware Monitor",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void SaveSettings()
    {
        try { SettingsService.Save(_settings); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
