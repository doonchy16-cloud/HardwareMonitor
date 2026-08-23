namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistryFileStore
{
    public ProfileRegistryFileStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Profile registry path cannot be blank.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath.Trim());
    }

    public string FilePath { get; }

    public async Task<ProfileRegistryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return ProfileRegistryDocument.Empty;
        }

        var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        return ProfileJsonSerializer.Deserialize(json);
    }

    public async Task SaveAsync(ProfileRegistryDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var json = ProfileJsonSerializer.Serialize(document);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
        var tempPath = Path.Combine(
            tempDirectory,
            $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
