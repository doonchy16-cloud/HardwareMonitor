using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheSpark.HardwareMonitor.App.Services;

namespace TheSpark.HardwareMonitor.App.Pages;

public partial class SensorsPage : UserControl
{
    public SensorsPage() => InitializeComponent();

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!MotionPreferences.Enabled || sender is not TabControl control || control.SelectedContent is not FrameworkElement content) return;
        content.Opacity = 0;
        content.RenderTransform = new TranslateTransform(10, 0);
        var duration = new Duration(MotionPreferences.Duration(220, 80));
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        ((TranslateTransform)content.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(10, 0, duration));
    }
}
