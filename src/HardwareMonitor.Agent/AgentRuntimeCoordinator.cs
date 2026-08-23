using TheSpark.HardwareMonitor.Core.Alerts;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Remote;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.Agent;

public sealed record AgentRuntimeCycleResult(
    bool RemoteFlushSucceeded,
    IReadOnlyList<AlertEvent> AlertEvents);

public sealed class AgentRuntimeCoordinator
{
    private readonly Guid _deviceId;
    private readonly IProfileRepository _profileRepository;
    private readonly RemoteTelemetryPublisher _publisher;
    private readonly AlertEngine _alertEngine;
    private readonly string _platform;
    private readonly string _agentVersion;

    public AgentRuntimeCoordinator(
        Guid deviceId,
        IProfileRepository profileRepository,
        RemoteTelemetryPublisher publisher,
        AlertEngine alertEngine,
        string platform,
        string agentVersion)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }

        ArgumentNullException.ThrowIfNull(profileRepository);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(alertEngine);
        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new ArgumentException("Platform must not be empty.", nameof(platform));
        }
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            throw new ArgumentException("Agent version must not be empty.", nameof(agentVersion));
        }

        _deviceId = deviceId;
        _profileRepository = profileRepository;
        _publisher = publisher;
        _alertEngine = alertEngine;
        _platform = platform;
        _agentVersion = agentVersion;
    }

    public async Task<AgentRuntimeCycleResult> ProcessSnapshotAsync(
        HardwareSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var load = await _profileRepository.LoadAsync().ConfigureAwait(false);
        if (!load.Success || load.Snapshot is null)
        {
            return new AgentRuntimeCycleResult(false, Array.Empty<AlertEvent>());
        }

        var profiles = load.Snapshot.Profiles
            .Where(profile => profile.Enabled && profile.DeviceId == _deviceId)
            .ToArray();

        var readings = snapshot.Devices
            .SelectMany(device => device.Sensors)
            .ToArray();
        var health = MapHealth(snapshot);
        var alertEvents = new List<AlertEvent>();

        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activity = profile.Capabilities.Contains(ProfileCapability.TrainingMode)
                ? ActivityState.Training
                : ActivityState.Idle;
            var status = ProfileStatusEvaluator.Evaluate(
                snapshot.CapturedAt,
                snapshot.CapturedAt,
                profile.FreshnessPolicy,
                activity,
                health);

            alertEvents.AddRange(_alertEngine.Evaluate(
                profile.ProfileId,
                status,
                readings,
                profile.ThermalThresholdPolicy,
                snapshot.CapturedAt));

            if (!profile.Capabilities.Contains(ProfileCapability.PublishHardwareTelemetry))
            {
                continue;
            }

            var metrics = readings
                .Where(reading =>
                    reading.Availability == SensorAvailability.Available &&
                    profile.SensorVisibilityPolicy.IsVisible(reading))
                .Select(reading => new TelemetryMetricEnvelope(
                    reading.Id,
                    reading.Name,
                    reading.Value,
                    null,
                    reading.Unit,
                    reading.Availability.ToString()))
                .ToArray();

            _publisher.QueueTelemetry(new TelemetryEnvelope(
                _deviceId,
                profile.ProfileId,
                snapshot.CapturedAt,
                activity.ToString(),
                health.ToString(),
                metrics));
        }

        if (profiles.Any(profile => profile.Capabilities.Contains(ProfileCapability.PublishDevicePresence)))
        {
            _publisher.QueuePresence(new PresenceEnvelope(
                _deviceId,
                snapshot.CapturedAt,
                _platform,
                _agentVersion,
                health == HealthState.Healthy ? "ONLINE" : "DEGRADED"));
        }

        var flushSucceeded = await _publisher.FlushOnceAsync(cancellationToken).ConfigureAwait(false);
        return new AgentRuntimeCycleResult(flushSucceeded, alertEvents);
    }

    private static HealthState MapHealth(HardwareSnapshot snapshot)
    {
        if (string.Equals(snapshot.EngineStatus, "Healthy", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            return HealthState.Healthy;
        }

        if (string.Equals(snapshot.EngineStatus, "Degraded", StringComparison.OrdinalIgnoreCase))
        {
            return HealthState.Degraded;
        }

        return HealthState.Error;
    }
}
