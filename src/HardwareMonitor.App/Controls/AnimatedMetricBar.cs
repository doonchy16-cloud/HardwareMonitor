using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TheSpark.HardwareMonitor.App.Services;

namespace TheSpark.HardwareMonitor.App.Controls;

public sealed class AnimatedMetricBar : Control
{
    private Border? _fill;

    static AnimatedMetricBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AnimatedMetricBar), new FrameworkPropertyMetadata(typeof(AnimatedMetricBar)));
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnimatedMetricBar), new FrameworkPropertyMetadata(0d, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fill = GetTemplateChild("PART_Fill") as Border;
        UpdateFill(false);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AnimatedMetricBar)d).UpdateFill(true);

    private void UpdateFill(bool animate)
    {
        if (_fill is null)
        {
            return;
        }

        var target = Math.Clamp(Value, 0, 100);
        if (!animate || !MotionPreferences.Enabled)
        {
            _fill.Width = target;
            return;
        }

        _fill.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = target,
            Duration = new Duration(MotionPreferences.Duration(450, 120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
