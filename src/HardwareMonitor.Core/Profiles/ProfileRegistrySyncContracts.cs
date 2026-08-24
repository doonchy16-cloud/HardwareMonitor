namespace TheSpark.HardwareMonitor.Core.Profiles;

public enum ProfileRegistrySyncStatus
{
    Current = 0,
    Stale = 1,
    PendingUpload = 2,
    Conflict = 3,
}

public sealed record ProfileRegistrySyncResult(
    ProfileRegistryDocument Registry,
    ProfileRegistrySyncStatus Status,
    long? RemoteRevision,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? ErrorCode);

public sealed record ProfileRegistrySyncMetadata
{
    public ProfileRegistrySyncMetadata(
        long lastKnownRemoteRevision,
        long? pendingBaseRevision,
        DateTimeOffset? lastSuccessfulSyncAt)
        : this(
            lastKnownRemoteRevision,
            pendingBaseRevision,
            lastSuccessfulSyncAt,
            pendingBaseRevision.HasValue ? ProfileRegistrySyncStatus.PendingUpload : ProfileRegistrySyncStatus.Stale,
            null,
            null)
    {
    }

    public ProfileRegistrySyncMetadata(
        long lastKnownRemoteRevision,
        long? pendingBaseRevision,
        DateTimeOffset? lastSuccessfulSyncAt,
        ProfileRegistrySyncStatus lastStatus,
        long? lastObservedRemoteRevision,
        string? lastErrorCode)
    {
        if (lastKnownRemoteRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastKnownRemoteRevision));
        }

        if (pendingBaseRevision is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingBaseRevision));
        }

        if (lastObservedRemoteRevision is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastObservedRemoteRevision));
        }

        if (!Enum.IsDefined(lastStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(lastStatus));
        }

        if (lastErrorCode is { Length: > 128 })
        {
            throw new ArgumentOutOfRangeException(nameof(lastErrorCode));
        }

        LastKnownRemoteRevision = lastKnownRemoteRevision;
        PendingBaseRevision = pendingBaseRevision;
        LastSuccessfulSyncAt = lastSuccessfulSyncAt;
        LastStatus = lastStatus;
        LastObservedRemoteRevision = lastObservedRemoteRevision;
        LastErrorCode = string.IsNullOrWhiteSpace(lastErrorCode) ? null : lastErrorCode.Trim();
    }

    public long LastKnownRemoteRevision { get; }

    public long? PendingBaseRevision { get; }

    public DateTimeOffset? LastSuccessfulSyncAt { get; }

    public ProfileRegistrySyncStatus LastStatus { get; }

    public long? LastObservedRemoteRevision { get; }

    public string? LastErrorCode { get; }

    public bool HasPendingChanges => PendingBaseRevision.HasValue;

    public static ProfileRegistrySyncMetadata Empty => new(
        0,
        null,
        null,
        ProfileRegistrySyncStatus.Stale,
        null,
        null);
}

public interface IProfileRegistryAuthorityClient
{
    Task<ProfileRegistryDocument> PullAsync(CancellationToken cancellationToken);

    Task<ProfileRegistryDocument> PushAsync(
        ProfileRegistryDocument registry,
        long expectedRevision,
        CancellationToken cancellationToken);
}

public sealed class ProfileRegistryAuthorityUnavailableException : Exception
{
    public ProfileRegistryAuthorityUnavailableException(string message)
        : base(message)
    {
    }

    public ProfileRegistryAuthorityUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ProfileRegistryRevisionConflictException : Exception
{
    public ProfileRegistryRevisionConflictException(long remoteRevision)
        : base($"Profile registry revision conflict. Remote revision is {remoteRevision}.")
    {
        if (remoteRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteRevision));
        }

        RemoteRevision = remoteRevision;
    }

    public long RemoteRevision { get; }
}
