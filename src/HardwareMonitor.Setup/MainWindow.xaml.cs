using System.Windows;
using System.Windows.Media.Animation;

namespace TheSpark.HardwareMonitor.Setup;

public partial class MainWindow : Window
{
    private readonly InstallerService _installer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _completed;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SparkDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.28, 1, TimeSpan.FromMilliseconds(750))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }

        InstallButton.IsEnabled = false;
        HeadingText.Text = "Installing Hardware Monitor";
        Progress.IsIndeterminate = true;

        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            var result = await _installer.InstallOrRepairAsync(progress, _lifetime.Token);
            _completed = true;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            HeadingText.Text = "Hardware Monitor is ready ✓";
            StatusText.Text = result.RebootRequired
                ? "Installation completed. Windows should be restarted when convenient so PawnIO can provide full sensor access. Hardware Monitor has been launched."
                : "Installation completed successfully. Hardware Monitor has been launched and Windows will manage future app updates.";
            InstallButton.Content = "Done";
            InstallButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            HeadingText.Text = "Setup cancelled";
            StatusText.Text = "No additional setup work will be started.";
            Progress.IsIndeterminate = false;
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            HeadingText.Text = "Setup could not finish";
            StatusText.Text = ex.Message;
            Progress.IsIndeterminate = false;
            InstallButton.Content = "Retry";
            InstallButton.IsEnabled = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
