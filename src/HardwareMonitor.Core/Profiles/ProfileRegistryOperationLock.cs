namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistryOperationLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    public ProfileRegistryOperationLock(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Profile registry operation lock path cannot be blank.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath.Trim());
    }

    public string FilePath { get; }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    FilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                return new Lease(stream);
            }
            catch (IOException)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        private FileStream? _stream = stream;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
