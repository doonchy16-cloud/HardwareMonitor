using TheSpark.HardwareMonitor.Core.Devices;

namespace TheSpark.HardwareMonitor.Core.Platforms;

public interface IPlatformTelemetryAdapter
{
    DevicePlatform Platform { get; }
    PlatformCapabilities Capabilities { get; }

    ValueTask<NormalizedTelemetrySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}
