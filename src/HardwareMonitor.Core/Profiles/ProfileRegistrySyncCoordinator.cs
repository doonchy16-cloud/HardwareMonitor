namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistrySyncCoordinator
{
    private readonly ProfileRegistryFileStore _registryStore;
    private readonly ProfileRegistrySyncMetadataFileStore _metadataStore;
    private readonly IProfileRegistryAuthorityClient _authority;
    private readonly ProfileRegistryOperationLock _operationLock;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public ProfileRegistrySyncCoordinator(ProfileRegistryFileStore registryStore, ProfileRegistrySyncMetadataFileStore metadataStore, IProfileRegistryAuthorityClient authority)
    {
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _operationLock = new ProfileRegistryOperationLock(_registryStore.FilePath + ".operation.lock");
    }

    public async Task<ProfileRegistrySyncResult> SaveLocalMutationAsync(ProfileRegistryDocument editedRegistry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editedRegistry);
        cancellationToken.ThrowIfCancellationRequested();
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var operationLease = await _operationLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            var current = await _registryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (editedRegistry.Revision != current.Revision)
                throw new InvalidOperationException("A local profile edit must preserve the cached authoritative revision.");
            var metadata = await _metadataStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var baseRevision = metadata.PendingBaseRevision ?? current.Revision;
            await _registryStore.SaveAsync(editedRegistry, cancellationToken).ConfigureAwait(false);
            await _metadataStore.SaveAsync(new ProfileRegistrySyncMetadata(
                Math.Max(metadata.LastKnownRemoteRevision, current.Revision), baseRevision, metadata.LastSuccessfulSyncAt,
                ProfileRegistrySyncStatus.PendingUpload, metadata.LastObservedRemoteRevision, null), cancellationToken).ConfigureAwait(false);
        }
        finally { _syncLock.Release(); }
        return await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProfileRegistrySyncResult> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProfileRegistryDocument? remote = null;
            string? pullErrorCode = null;
            try { remote = await _authority.PullAsync(cancellationToken).ConfigureAwait(false); }
            catch (ProfileRegistryAuthorityUnavailableException ex) { pullErrorCode = ex.GetType().Name; }

            await using var operationLease = await _operationLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            var local = await _registryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var metadata = await _metadataStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                var status = metadata.HasPendingChanges ? ProfileRegistrySyncStatus.PendingUpload : ProfileRegistrySyncStatus.Stale;
                return await PersistResultAsync(local, status, null, metadata.LastSuccessfulSyncAt, pullErrorCode,
                    metadata, metadata.PendingBaseRevision, cancellationToken).ConfigureAwait(false);
            }

            if (metadata.PendingBaseRevision is long pendingBase)
            {
                if (remote.Revision != pendingBase)
                    return await PersistResultAsync(local, ProfileRegistrySyncStatus.Conflict, remote.Revision,
                        metadata.LastSuccessfulSyncAt, nameof(ProfileRegistryRevisionConflictException), metadata,
                        pendingBase, cancellationToken).ConfigureAwait(false);
                try
                {
                    var committed = await _authority.PushAsync(local, pendingBase, cancellationToken).ConfigureAwait(false);
                    if (committed.Revision != checked(pendingBase + 1))
                        throw new InvalidDataException("Authority did not advance the registry revision exactly once.");
                    var now = DateTimeOffset.UtcNow;
                    await _registryStore.SaveAsync(committed, cancellationToken).ConfigureAwait(false);
                    return await PersistResultAsync(committed, ProfileRegistrySyncStatus.Current, committed.Revision,
                        now, null, metadata, null, cancellationToken).ConfigureAwait(false);
                }
                catch (ProfileRegistryRevisionConflictException ex)
                {
                    return await PersistResultAsync(local, ProfileRegistrySyncStatus.Conflict, ex.RemoteRevision,
                        metadata.LastSuccessfulSyncAt, ex.GetType().Name, metadata, pendingBase, cancellationToken).ConfigureAwait(false);
                }
                catch (ProfileRegistryAuthorityUnavailableException ex)
                {
                    return await PersistResultAsync(local, ProfileRegistrySyncStatus.PendingUpload, null,
                        metadata.LastSuccessfulSyncAt, ex.GetType().Name, metadata, pendingBase, cancellationToken).ConfigureAwait(false);
                }
            }

            if (remote.Revision == 0 && remote.Profiles.Count == 0 && local.Revision == 0 && local.Profiles.Count > 0)
            {
                try
                {
                    var committed = await _authority.PushAsync(local, 0, cancellationToken).ConfigureAwait(false);
                    if (committed.Revision != 1)
                        throw new InvalidDataException("Authority bootstrap must advance registry revision from 0 to 1.");
                    var now = DateTimeOffset.UtcNow;
                    await _registryStore.SaveAsync(committed, cancellationToken).ConfigureAwait(false);
                    return await PersistResultAsync(committed, ProfileRegistrySyncStatus.Current, 1, now, null,
                        metadata, null, cancellationToken).ConfigureAwait(false);
                }
                catch (ProfileRegistryAuthorityUnavailableException ex)
                {
                    return await PersistResultAsync(local, ProfileRegistrySyncStatus.Stale, null,
                        metadata.LastSuccessfulSyncAt, ex.GetType().Name, metadata, null, cancellationToken).ConfigureAwait(false);
                }
            }

            if (remote.Revision > local.Revision)
            {
                var now = DateTimeOffset.UtcNow;
                await _registryStore.SaveAsync(remote, cancellationToken).ConfigureAwait(false);
                return await PersistResultAsync(remote, ProfileRegistrySyncStatus.Current, remote.Revision, now,
                    null, metadata, null, cancellationToken).ConfigureAwait(false);
            }

            if (remote.Revision < local.Revision)
                return await PersistResultAsync(local, ProfileRegistrySyncStatus.Conflict, remote.Revision,
                    metadata.LastSuccessfulSyncAt, nameof(ProfileRegistryRevisionConflictException), metadata, null,
                    cancellationToken).ConfigureAwait(false);

            var syncedAt = DateTimeOffset.UtcNow;
            return await PersistResultAsync(local, ProfileRegistrySyncStatus.Current, remote.Revision, syncedAt,
                null, metadata, null, cancellationToken).ConfigureAwait(false);
        }
        finally { _syncLock.Release(); }
    }

    private async Task<ProfileRegistrySyncResult> PersistResultAsync(
        ProfileRegistryDocument registry,
        ProfileRegistrySyncStatus status,
        long? remoteRevision,
        DateTimeOffset? lastSuccessfulSyncAt,
        string? errorCode,
        ProfileRegistrySyncMetadata previous,
        long? pendingBaseRevision,
        CancellationToken cancellationToken)
    {
        var knownRemoteRevision = remoteRevision is long observed
            ? Math.Max(previous.LastKnownRemoteRevision, observed)
            : previous.LastKnownRemoteRevision;
        await _metadataStore.SaveAsync(new ProfileRegistrySyncMetadata(
            knownRemoteRevision, pendingBaseRevision, lastSuccessfulSyncAt, status, remoteRevision, errorCode),
            cancellationToken).ConfigureAwait(false);
        return new ProfileRegistrySyncResult(registry, status, remoteRevision, lastSuccessfulSyncAt, errorCode);
    }
}
