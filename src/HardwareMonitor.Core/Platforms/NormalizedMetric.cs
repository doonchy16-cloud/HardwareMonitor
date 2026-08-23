using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Platforms;

public sealed class NormalizedMetric
{
    public NormalizedMetric(
        string key,
        string label,
        double? numericValue,
        string? textValue,
        string unit,
        DateTimeOffset capturedAt,
        SensorAvailability availability)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Metric key must not be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Metric label must not be empty.", nameof(label));
        }

        Key = key.Trim();
        Label = label.Trim();
        NumericValue = numericValue;
        TextValue = textValue;
        Unit = unit ?? string.Empty;
        CapturedAt = capturedAt;
        Availability = availability;
    }

    public string Key { get; }
    public string Label { get; }
    public double? NumericValue { get; }
    public string? TextValue { get; }
    public string Unit { get; }
    public DateTimeOffset CapturedAt { get; }
    public SensorAvailability Availability { get; }
}
