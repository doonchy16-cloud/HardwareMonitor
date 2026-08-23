using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.Core.Alerts;

public sealed class AlertEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ActiveAlert> _activeByProfile = [];

    public IReadOnlyList<AlertEvent> Evaluate(
        Guid profileId,
        ProfileStatus status,
        IReadOnlyCollection<SensorReading> readings,
        ThermalThresholdPolicy thermalThresholds,
        DateTimeOffset now)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(thermalThresholds);

        var candidate = DetermineCandidate(status, readings, thermalThresholds);

        lock (_gate)
        {
            _activeByProfile.TryGetValue(profileId, out var prior);

            if (candidate is null)
            {
                if (prior is null)
                {
                    return Array.Empty<AlertEvent>();
                }

                _activeByProfile.Remove(profileId);
                return new[]
                {
                    new AlertEvent(
                        profileId,
                        AlertKind.Recovered,
                        now,
                        $"Recovered from {prior.Kind}.",
                        RecoveredKind: prior.Kind)
                };
            }

            if (prior is not null && prior.Kind == candidate.Kind)
            {
                return Array.Empty<AlertEvent>();
            }

            _activeByProfile[profileId] = candidate;
            return new[]
            {
                new AlertEvent(
                    profileId,
                    candidate.Kind,
                    now,
                    candidate.Message,
                    candidate.SensorId,
                    candidate.TemperatureCelsius)
            };
        }
    }

    private static ActiveAlert? DetermineCandidate(
        ProfileStatus status,
        IReadOnlyCollection<SensorReading> readings,
        ThermalThresholdPolicy thresholds)
    {
        if (status.Connectivity == ConnectivityState.Offline)
        {
            return new ActiveAlert(AlertKind.DeviceOffline, "Device is offline.");
        }

        if (status.Connectivity == ConnectivityState.Stale)
        {
            return new ActiveAlert(AlertKind.TelemetryStale, "Telemetry is stale.");
        }

        if (status.Health == HealthState.Error)
        {
            return new ActiveAlert(AlertKind.SensorError, "Sensor engine reported an error.");
        }

        if (status.Health == HealthState.Degraded)
        {
            return new ActiveAlert(AlertKind.SensorDegraded, "Some sensor telemetry is degraded.");
        }

        var hottest = readings
            .Where(reading =>
                reading.Kind == SensorKind.Temperature &&
                reading.Availability == SensorAvailability.Available &&
                reading.Value.HasValue &&
                !double.IsNaN(reading.Value.Value) &&
                !double.IsInfinity(reading.Value.Value))
            .OrderByDescending(reading => reading.Value!.Value)
            .FirstOrDefault();

        if (hottest is null)
        {
            return null;
        }

        var value = hottest.Value!.Value;
        if (value >= thresholds.CriticalCelsius)
        {
            return new ActiveAlert(
                AlertKind.ThermalCritical,
                $"{hottest.Name} is critical at {value:0.#} °C.",
                hottest.Id,
                value);
        }

        if (value >= thresholds.HotCelsius)
        {
            return new ActiveAlert(
                AlertKind.ThermalHot,
                $"{hottest.Name} is hot at {value:0.#} °C.",
                hottest.Id,
                value);
        }

        if (value >= thresholds.WarmCelsius)
        {
            return new ActiveAlert(
                AlertKind.ThermalWarm,
                $"{hottest.Name} is warm at {value:0.#} °C.",
                hottest.Id,
                value);
        }

        return null;
    }

    private sealed record ActiveAlert(
        AlertKind Kind,
        string Message,
        string? SensorId = null,
        double? TemperatureCelsius = null);
}
