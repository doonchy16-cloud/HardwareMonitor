using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public interface ISensorProvider : IAsyncDisposable
{
    Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken);
}
