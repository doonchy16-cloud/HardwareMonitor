namespace TheSpark.HardwareMonitor.Core.Profiles;

public interface IProfileRepository
{
    Task<ProfileRepositoryLoadResult> LoadAsync();
    Task SaveAsync(ProfileRegistrySnapshot snapshot);
}
