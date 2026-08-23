using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed record ProfileMetricRowViewModel(
    string Id,
    string Name,
    SensorKind Kind,
    double Value,
    string Unit);

public sealed record ProfileCardViewModel(
    Guid ProfileId,
    string Name,
    ConnectivityState Connectivity,
    ActivityState Activity,
    HealthState Health,
    string StatusText,
    string LastSeenText,
    bool ShowMetrics,
    bool IsHistorical,
    IReadOnlyList<ProfileMetricRowViewModel> Metrics);
