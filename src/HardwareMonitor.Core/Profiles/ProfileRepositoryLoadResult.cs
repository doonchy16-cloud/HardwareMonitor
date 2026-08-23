namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed record ProfileRepositoryLoadResult(
    bool Success,
    ProfileRegistrySnapshot? Snapshot,
    string? Error)
{
    public static ProfileRepositoryLoadResult Loaded(ProfileRegistrySnapshot snapshot) =>
        new(true, snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static ProfileRepositoryLoadResult Failed(string error) =>
        new(false, null, string.IsNullOrWhiteSpace(error) ? "Profile cache load failed." : error);
}
