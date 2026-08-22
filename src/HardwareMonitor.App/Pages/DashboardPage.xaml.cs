using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheSpark.HardwareMonitor.App.Services;

namespace TheSpark.HardwareMonitor.App.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage() => InitializeComponent();

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (!MotionPreferences.Enabled) return;
        var cards = new FrameworkElement[] { CpuCard, GpuCard, MemoryCard, StorageCard };
        for (var index = 0; index < cards.Length; index++) AnimateIn(cards[index], index * 65);
        LiveDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1.0, new Duration(MotionPreferences.Duration(800, 250))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    private static void AnimateIn(FrameworkElement element, int delayMilliseconds)
    {
        element.Opacity = 0;
        element.RenderTransform = new TranslateTransform(0, 14);
        var duration = MotionPreferences.Duration(360, 100);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(duration)) { BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, new Duration(duration)) { BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
