using TheSpark.HardwareMonitor.Core.Devices;

namespace TheSpark.HardwareMonitor.Core.Platforms;

public sealed class PlatformCapabilities
{
    public PlatformCapabilities(
        DevicePlatform platform,
        IReadOnlySet<PlatformTelemetryCapability> telemetryCapabilities)
    {
        ArgumentNullException.ThrowIfNull(telemetryCapabilities);
        Platform = platform;
        TelemetryCapabilities = new HashSet<PlatformTelemetryCapability>(telemetryCapabilities);
    }

    public DevicePlatform Platform { get; }
    public IReadOnlySet<PlatformTelemetryCapability> TelemetryCapabilities { get; }

    public bool Supports(PlatformTelemetryCapability capability) =>
        TelemetryCapabilities.Contains(capability);
}
