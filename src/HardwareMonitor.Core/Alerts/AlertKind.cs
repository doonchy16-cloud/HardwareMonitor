namespace TheSpark.HardwareMonitor.Core.Alerts;

public enum AlertKind
{
    ThermalWarm,
    ThermalHot,
    ThermalCritical,
    TelemetryStale,
    DeviceOffline,
    SensorDegraded,
    SensorError,
    Recovered
}
