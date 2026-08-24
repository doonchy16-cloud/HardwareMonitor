using System.Text.Json;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistrySyncMetadataFileStore
{
    private const string SchemaVersion = "1.0";
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public ProfileRegistrySyncMetadataFileStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Profile sync metadata path cannot be blank.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath.Trim());
    }

    public string FilePath { get; }

    public async Task<ProfileRegistrySyncMetadata> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return ProfileRegistrySyncMetadata.Empty;
            }

            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("schema_version", out var schema)
                || schema.GetString() != SchemaVersion)
            {
                throw new InvalidDataException("Profile sync metadata schema is unsupported.");
            }

            if (!root.TryGetProperty("last_known_remote_revision", out var remoteElement)
                || !remoteElement.TryGetInt64(out var remoteRevision)
                || remoteRevision < 0)
            {
                throw new InvalidDataException("Profile sync metadata remote revision is invalid.");
            }

            long? pendingBase = null;
            if (root.TryGetProperty("pending_base_revision", out var pendingElement)
                && pendingElement.ValueKind != JsonValueKind.Null)
            {
                if (!pendingElement.TryGetInt64(out var value) || value < 0)
                {
                    throw new InvalidDataException("Profile sync metadata pending revision is invalid.");
                }
                pendingBase = value;
            }

            DateTimeOffset? lastSync = null;
            if (root.TryGetProperty("last_successful_sync_at", out var syncElement)
                && syncElement.ValueKind != JsonValueKind.Null)
            {
                if (syncElement.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(syncElement.GetString(), out var parsedSync))
                {
                    throw new InvalidDataException("Profile sync metadata timestamp is invalid.");
                }
                lastSync = parsedSync;
            }

            var lastStatus = pendingBase.HasValue
                ? ProfileRegistrySyncStatus.PendingUpload
                : ProfileRegistrySyncStatus.Stale;
            if (root.TryGetProperty("last_status", out var statusElement)
                && statusElement.ValueKind != JsonValueKind.Null)
            {
                if (statusElement.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<ProfileRegistrySyncStatus>(statusElement.GetString(), ignoreCase: false, out lastStatus)
                    || !Enum.IsDefined(lastStatus))
                {
                    throw new InvalidDataException("Profile sync metadata status is invalid.");
                }
            }

            long? lastObservedRemoteRevision = null;
            if (root.TryGetProperty("last_observed_remote_revision", out var observedElement)
                && observedElement.ValueKind != JsonValueKind.Null)
            {
                if (!observedElement.TryGetInt64(out var observed) || observed < 0)
                {
                    throw new InvalidDataException("Profile sync metadata observed remote revision is invalid.");
                }
                lastObservedRemoteRevision = observed;
            }

            string? lastErrorCode = null;
            if (root.TryGetProperty("last_error_code", out var errorElement)
                && errorElement.ValueKind != JsonValueKind.Null)
            {
                if (errorElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Profile sync metadata error code is invalid.");
                }
                lastErrorCode = errorElement.GetString();
                if (lastErrorCode is { Length: > 128 })
                {
                    throw new InvalidDataException("Profile sync metadata error code is too long.");
                }
            }

            return new ProfileRegistrySyncMetadata(
                remoteRevision,
                pendingBase,
                lastSync,
                lastStatus,
                lastObservedRemoteRevision,
                lastErrorCode);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(ProfileRegistrySyncMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion,
            last_known_remote_revision = metadata.LastKnownRemoteRevision,
            pending_base_revision = metadata.PendingBaseRevision,
            last_successful_sync_at = metadata.LastSuccessfulSyncAt?.ToString("O"),
            last_status = metadata.LastStatus.ToString(),
            last_observed_remote_revision = metadata.LastObservedRemoteRevision,
            last_error_code = metadata.LastErrorCode,
        });

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var tempDirectory = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
            var tempPath = Path.Combine(tempDirectory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, FilePath, true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
