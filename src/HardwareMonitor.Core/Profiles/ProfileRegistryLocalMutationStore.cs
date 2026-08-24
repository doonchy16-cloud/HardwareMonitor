namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistryLocalMutationStore
{
    private readonly ProfileRegistryFileStore _registryStore;
    private readonly ProfileRegistrySyncMetadataFileStore _metadataStore;
    private readonly ProfileRegistryOperationLock _operationLock;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    public ProfileRegistryLocalMutationStore(
        ProfileRegistryFileStore registryStore,
        ProfileRegistrySyncMetadataFileStore metadataStore)
    {
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _operationLock = new ProfileRegistryOperationLock(_registryStore.FilePath + ".operation.lock");
    }

    public async Task<ProfileRegistrySyncMetadata> SaveAsync(
        ProfileRegistryDocument editedRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editedRegistry);
        cancellationToken.ThrowIfCancellationRequested();
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var operationLease = await _operationLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            var current = await _registryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (editedRegistry.Revision != current.Revision)
            {
                throw new InvalidOperationException("A local profile edit must preserve the cached authoritative revision.");
            }

            var metadata = await _metadataStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var updated = new ProfileRegistrySyncMetadata(
                Math.Max(metadata.LastKnownRemoteRevision, current.Revision),
                metadata.PendingBaseRevision ?? current.Revision,
                metadata.LastSuccessfulSyncAt,
                ProfileRegistrySyncStatus.PendingUpload,
                metadata.LastObservedRemoteRevision,
                null);

            await _registryStore.SaveAsync(editedRegistry, cancellationToken).ConfigureAwait(false);
            await _metadataStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _mutationLock.Release();
        }
    }
}
