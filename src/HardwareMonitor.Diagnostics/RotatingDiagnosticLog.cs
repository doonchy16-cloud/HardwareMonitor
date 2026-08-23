using System.Text;

namespace TheSpark.HardwareMonitor.Diagnostics;

public sealed class RotatingDiagnosticLog
{
    private const long DefaultMaxBytes = 2L * 1024 * 1024;
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _retainedFiles;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RotatingDiagnosticLog(
        string? directory = null,
        string baseName = "hardwaremonitor.log",
        long maxBytes = DefaultMaxBytes,
        int retainedFiles = 3)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (retainedFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        }

        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "The Spark",
            "Hardware Monitor",
            "logs");
        _baseName = baseName;
        _maxBytes = maxBytes;
        _retainedFiles = retainedFiles;
    }

    public string CurrentLogPath => Path.Combine(_directory, _baseName);

    public async Task WriteAsync(string level, string message, CancellationToken cancellationToken = default)
    {
        var safeLevel = DiagnosticSanitizer.SanitizeValue(level, 24);
        var safeMessage = DiagnosticSanitizer.SanitizeValue(message, 2048);
        var line = $"{DateTimeOffset.UtcNow:O} [{safeLevel}] {safeMessage}{Environment.NewLine}";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
            await File.AppendAllTextAsync(CurrentLogPath, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var file = new FileInfo(CurrentLogPath);
        if (!file.Exists || file.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        for (var index = _retainedFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? CurrentLogPath : $"{CurrentLogPath}.{index - 1}";
            var destination = $"{CurrentLogPath}.{index}";
            if (!File.Exists(source))
            {
                continue;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }
    }
}
