namespace TheSpark.HardwareMonitor.Core.Alerts;

public sealed record AlertEvent(
    Guid ProfileId,
    AlertKind Kind,
    DateTimeOffset OccurredAt,
    string Message,
    string? SensorId = null,
    double? TemperatureCelsius = null,
    AlertKind? RecoveredKind = null);
