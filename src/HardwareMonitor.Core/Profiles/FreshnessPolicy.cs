namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed record FreshnessPolicy
{
    public FreshnessPolicy(TimeSpan staleAfter, TimeSpan offlineAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), "Stale threshold must be positive.");
        }

        if (offlineAfter <= staleAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineAfter), "Offline threshold must be later than stale threshold.");
        }

        StaleAfter = staleAfter;
        OfflineAfter = offlineAfter;
    }

    public TimeSpan StaleAfter { get; }
    public TimeSpan OfflineAfter { get; }
}
