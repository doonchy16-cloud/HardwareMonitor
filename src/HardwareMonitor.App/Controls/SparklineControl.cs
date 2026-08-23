using System.Collections;
using System.Windows;
using System.Windows.Media;
using TheSpark.HardwareMonitor.Core;

namespace TheSpark.HardwareMonitor.App.Controls;

public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(object), typeof(SparklineControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SparklineControl), new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public object? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var values = ReadValues().ToArray();
        if (values.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(1d, max - min);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < values.Length; index++)
            {
                var x = index * ActualWidth / Math.Max(1, values.Length - 1);
                var y = ActualHeight - ((values[index] - min) / range * Math.Max(1, ActualHeight - 4)) - 2;
                var point = new Point(x, y);
                if (index == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 2), geometry);
    }

    private IEnumerable<double> ReadValues()
    {
        if (Values is RollingSeries series)
        {
            return series.Values;
        }

        if (Values is IEnumerable<double> doubles)
        {
            return doubles;
        }

        if (Values is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().OfType<double>();
        }

        return Array.Empty<double>();
    }
}
