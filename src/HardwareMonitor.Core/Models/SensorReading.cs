namespace TheSpark.HardwareMonitor.Core.Models;

public sealed record SensorReading(
    string Id,
    string Name,
    SensorKind Kind,
    double? Value,
    string Unit,
    DateTimeOffset CapturedAt,
    SensorAvailability Availability)
{
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Availability != SensorAvailability.Available || now - CapturedAt > staleAfter;

    public bool IsLive(DateTimeOffset now, TimeSpan staleAfter) =>
        Availability == SensorAvailability.Available && Value.HasValue && !IsStale(now, staleAfter);
}
