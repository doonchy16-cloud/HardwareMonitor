using System.Text.Json;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistryFileStore
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public ProfileRegistryFileStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Profile registry path cannot be blank.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath.Trim());
        BackupFilePath = FilePath + ".bak";
    }

    public string FilePath { get; }

    public string BackupFilePath { get; }

    public async Task<ProfileRegistryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return File.Exists(BackupFilePath)
                    ? await RecoverFromBackupAsync(cancellationToken).ConfigureAwait(false)
                    : ProfileRegistryDocument.Empty;
            }

            try
            {
                var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
                return ProfileJsonSerializer.Deserialize(json);
            }
            catch (Exception ex) when (IsRecoverableCacheException(ex) && File.Exists(BackupFilePath))
            {
                return await RecoverFromBackupAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(ProfileRegistryDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var json = ProfileJsonSerializer.Serialize(document);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicallyAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(BackupFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<ProfileRegistryDocument> RecoverFromBackupAsync(CancellationToken cancellationToken)
    {
        var backupJson = await File.ReadAllTextAsync(BackupFilePath, cancellationToken).ConfigureAwait(false);
        var document = ProfileJsonSerializer.Deserialize(backupJson);
        await WriteAtomicallyAsync(FilePath, backupJson, cancellationToken).ConfigureAwait(false);
        return document;
    }

    private static bool IsRecoverableCacheException(Exception exception) =>
        exception is JsonException or NotSupportedException or ArgumentException or InvalidDataException;

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
        var tempPath = Path.Combine(tempDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, true);
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
