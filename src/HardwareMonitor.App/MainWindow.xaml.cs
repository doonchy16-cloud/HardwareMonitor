using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheSpark.HardwareMonitor.App.Pages;
using TheSpark.HardwareMonitor.App.Services;
using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Diagnostics;
using TheSpark.HardwareMonitor.Platform.Windows;
using TheSpark.HardwareMonitor.Sensors;

namespace TheSpark.HardwareMonitor.App;

public partial class MainWindow : Window
{
    private readonly HardwareMonitorService _monitorService;
    private readonly SystemInventoryProvider _inventoryProvider;
    private readonly RotatingDiagnosticLog _log;
    private readonly AppSettings _settings;
    private readonly TelemetryViewModel _telemetry = new();
    private readonly HardwareViewModel _hardware = new();
    private readonly Dictionary<string, UserControl> _pages;
    private bool _drawerOpen;
    private HardwareSnapshot? _lastSnapshot;

    public MainWindow(
        HardwareMonitorService monitorService,
        SystemInventoryProvider inventoryProvider,
        RotatingDiagnosticLog log,
        AppSettings settings,
        BackgroundAgentController backgroundAgentController)
    {
        InitializeComponent();
        _monitorService = monitorService;
        _inventoryProvider = inventoryProvider;
        _log = log;
        _settings = settings;
        DataContext = _telemetry;

        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            Left = settings.WindowLeft.Value;
            Top = settings.WindowTop.Value;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        var dashboard = new DashboardPage { DataContext = _telemetry };
        var sensors = new SensorsPage { DataContext = _telemetry };
        var hardware = new HardwarePage { DataContext = _hardware };
        var profiles = new ProfilesPage();
        var settingsPage = new SettingsPage(_monitorService, _telemetry, _log, _settings, backgroundAgentController);
        settingsPage.TemperatureUnitChanged += value =>
        {
            _telemetry.TemperatureUnit = value;
            if (_lastSnapshot is not null)
            {
                _telemetry.Apply(_lastSnapshot);
            }
        };

        _telemetry.TemperatureUnit = settings.TemperatureUnit;
        _pages = new Dictionary<string, UserControl>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = dashboard,
            ["Sensors"] = sensors,
            ["Hardware"] = hardware,
            ["Profiles"] = profiles,
            ["Settings"] = settingsPage
        };

        _monitorService.SnapshotUpdated += MonitorService_SnapshotUpdated;
        Navigate(_pages.ContainsKey(settings.LastPage) ? settings.LastPage : "Dashboard", false);
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartHeaderPulse();
        try
        {
            var snapshot = await _inventoryProvider.GetSnapshotAsync();
            _hardware.Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await _log.WriteAsync("WARN", $"Inventory scan failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void MonitorService_SnapshotUpdated(HardwareSnapshot snapshot)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _lastSnapshot = snapshot;
            _telemetry.Apply(snapshot);
        });
    }

    private void BurgerButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateBurger();
        SetDrawer(!_drawerOpen);
    }

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => SetDrawer(false);

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            Navigate(page, true);
        }
    }

    private void Navigate(string page, bool closeDrawer)
    {
        if (!_pages.TryGetValue(page, out var control))
        {
            return;
        }

        _settings.LastPage = page;
        PageHost.Content = control;
        AnimatePage(control);
        if (closeDrawer)
        {
            SetDrawer(false);
        }
    }

    private void SetDrawer(bool open)
    {
        _drawerOpen = open;
        if (open)
        {
            Scrim.Visibility = Visibility.Visible;
        }

        var duration = new Duration(MotionPreferences.Duration(280, 100));
        if (!MotionPreferences.Enabled)
        {
            DrawerTransform.X = open ? 0 : -300;
            Scrim.Opacity = open ? 1 : 0;
            Scrim.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        DrawerTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(open ? 0 : -300, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        var scrimAnimation = new DoubleAnimation(open ? 1 : 0, duration);
        if (!open)
        {
            scrimAnimation.Completed += (_, _) => Scrim.Visibility = Visibility.Collapsed;
        }
        Scrim.BeginAnimation(OpacityProperty, scrimAnimation);
    }

    private void AnimateBurger()
    {
        if (!MotionPreferences.Enabled)
        {
            return;
        }

        var pop = new DoubleAnimationUsingKeyFrames();
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(MotionPreferences.Duration(90, 45))));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(MotionPreferences.Duration(180, 80))));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(MotionPreferences.Duration(260, 110))));
        BurgerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BurgerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private static void AnimatePage(FrameworkElement page)
    {
        if (!MotionPreferences.Enabled)
        {
            page.Opacity = 1;
            return;
        }

        page.Opacity = 0;
        page.RenderTransform = new TranslateTransform(16, 0);
        var duration = new Duration(MotionPreferences.Duration(260, 90));
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void StartHeaderPulse()
    {
        if (!MotionPreferences.Enabled)
        {
            return;
        }

        HeaderLiveDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, new Duration(MotionPreferences.Duration(900, 300)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drawerOpen)
        {
            SetDrawer(false);
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _monitorService.SnapshotUpdated -= MonitorService_SnapshotUpdated;
        _settings.WindowWidth = ActualWidth;
        _settings.WindowHeight = ActualHeight;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        try
        {
            SettingsService.Save(_settings);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
