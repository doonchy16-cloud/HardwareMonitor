namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistryFileStore
{
    public ProfileRegistryFileStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public Task<ProfileRegistryDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task SaveAsync(ProfileRegistryDocument document, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
