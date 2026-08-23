using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Ipc;

namespace TheSpark.HardwareMonitor.Agent;

public sealed class LocalIpcServer : IAsyncDisposable
{
    public const string DefaultPipeName = "TheSpark.HardwareMonitor.Agent.v1";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly AgentHost _host;
    private readonly string _pipeName;
    private int _disposed;

    public LocalIpcServer(AgentHost host, string? pipeName = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName.Trim();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                4096,
                4096);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A desktop client may disappear at any time. Monitoring remains independent.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        AgentMessage response;
        try
        {
            var line = await ReadBoundedLineAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            var request = JsonSerializer.Deserialize<AgentMessage>(line)
                ?? throw new InvalidDataException("IPC request is empty.");
            ValidateRequest(request);

            var health = _host.Health;
            var status = new AgentStatusPayload(
                health.State.ToString(),
                health.UpdatedAt,
                health.LastSuccessfulReadAt,
                health.ConsecutiveFailures,
                health.ErrorMessage,
                _host.LatestSnapshot);
            response = new AgentMessage(
                AgentProtocol.Version,
                AgentProtocol.Status,
                request.RequestId,
                status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or DecoderFallbackException or ArgumentException)
        {
            response = new AgentMessage(
                AgentProtocol.Version,
                AgentProtocol.Error,
                string.Empty,
                null,
                ex.Message);
        }

        await WriteMessageAsync(pipe, response, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRequest(AgentMessage request)
    {
        if (!string.Equals(request.ProtocolVersion, AgentProtocol.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported IPC protocol version.");
        }
        if (!string.Equals(request.Type, AgentProtocol.GetStatus, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported IPC request type.");
        }
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
        {
            throw new InvalidDataException("IPC request ID is invalid.");
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : StrictUtf8.GetString(buffer.ToArray());
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                buffer.WriteByte(one[0]);
            }

            if (buffer.Length > AgentProtocol.MaxMessageBytes)
            {
                throw new InvalidDataException("IPC message is too large.");
            }
        }

        return StrictUtf8.GetString(buffer.ToArray());
    }

    private static async Task WriteMessageAsync(Stream stream, AgentMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(message);
        var bytes = StrictUtf8.GetBytes(payload + "\n");
        if (bytes.Length > AgentProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException("IPC response is too large.");
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
