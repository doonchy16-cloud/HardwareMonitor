using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class SensorVisibilityPolicy
{
    public static SensorVisibilityPolicy All { get; } =
        new(new HashSet<SensorKind>());

    public SensorVisibilityPolicy(IReadOnlySet<SensorKind> visibleKinds)
    {
        ArgumentNullException.ThrowIfNull(visibleKinds);
        VisibleKinds = new HashSet<SensorKind>(visibleKinds);
    }

    public IReadOnlySet<SensorKind> VisibleKinds { get; }

    public bool IsVisible(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return VisibleKinds.Count == 0 || VisibleKinds.Contains(reading.Kind);
    }
}
