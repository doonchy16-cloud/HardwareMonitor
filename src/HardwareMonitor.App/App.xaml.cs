using System.IO;
using System.Threading;
using System.Windows;
using TheSpark.HardwareMonitor.App.Services;
using TheSpark.HardwareMonitor.Diagnostics;
using TheSpark.HardwareMonitor.Platform.Windows;
using TheSpark.HardwareMonitor.Sensors;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.App;

public partial class App : Application
{
    private const string MutexName = "TheSpark.HardwareMonitor.SingleInstance";
    private const string ActivateEventName = "TheSpark.HardwareMonitor.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _activationCts;
    private HardwareMonitorService? _monitorService;
    private BackgroundAgentController? _backgroundAgentController;
    private RotatingDiagnosticLog? _log;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activationCts = new CancellationTokenSource();

        var settings = SettingsService.Load();
        ThemeManager.Apply(settings.Theme);
        MotionPreferences.Level = settings.Motion;

        _log = new RotatingDiagnosticLog();
        var provider = new LibreHardwareMonitorProvider();
        _monitorService = new HardwareMonitorService(provider, TimeSpan.FromMilliseconds(settings.PollIntervalMilliseconds));
        var inventoryProvider = new SystemInventoryProvider();
        _backgroundAgentController = new BackgroundAgentController(
            Path.Combine(AppContext.BaseDirectory, "HardwareMonitor.Agent.exe"),
            AgentIpcProtocol.DefaultPipeName);

        var window = new MainWindow(_monitorService, inventoryProvider, _log, settings, _backgroundAgentController);
        MainWindow = window;
        window.Show();

        _ = ListenForActivationAsync(window, _activationCts.Token);
        _ = _monitorService.StartAsync();
        if (settings.StartBackgroundAgentWithDesktop)
        {
            _ = EnsureBackgroundAgentAsync(settings);
        }

        _ = _log.WriteAsync("INFO", "Hardware Monitor started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activateEvent?.Dispose();

        if (_monitorService is not null)
        {
            _monitorService.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private async Task EnsureBackgroundAgentAsync(AppSettings settings)
    {
        var controller = _backgroundAgentController;
        var log = _log;
        if (controller is null || log is null)
        {
            return;
        }

        try
        {
            var health = await controller.EnsureRunningAsync(
                TimeSpan.FromMilliseconds(settings.PollIntervalMilliseconds));
            if (health.State == AgentLifecycleState.Faulted)
            {
                await log.WriteAsync("WARN", $"Background agent faulted: {health.LastError ?? "UnknownFault"}.");
            }
            else
            {
                await log.WriteAsync("INFO", $"Background agent state: {health.State}.");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await log.WriteAsync("WARN", $"Background agent unavailable: {ex.GetType().Name}.");
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivateEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private async Task ListenForActivationAsync(Window window, CancellationToken cancellationToken)
    {
        var activateEvent = _activateEvent;
        if (activateEvent is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = await Task.Run(() => activateEvent.WaitOne(TimeSpan.FromSeconds(1)), cancellationToken)
                .ConfigureAwait(false);
            if (!signaled)
            {
                continue;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Show();
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();
            });
        }
    }
}