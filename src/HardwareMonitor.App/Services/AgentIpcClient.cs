using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Ipc;

namespace TheSpark.HardwareMonitor.App.Services;

public sealed class AgentIpcClient
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public AgentIpcClient(string? pipeName = null, TimeSpan? connectTimeout = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? "TheSpark.HardwareMonitor.Agent.v1"
            : pipeName.Trim();
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2);
        if (_connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }
    }

    public async Task<AgentStatusPayload> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_connectTimeout);
        await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

        var requestId = Guid.NewGuid().ToString("N");
        var request = new AgentMessage(
            AgentProtocol.Version,
            AgentProtocol.GetStatus,
            requestId);
        await WriteMessageAsync(pipe, request, cancellationToken).ConfigureAwait(false);

        var line = await ReadBoundedLineAsync(pipe, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Hardware Monitor agent closed the IPC connection.");
        var response = JsonSerializer.Deserialize<AgentMessage>(line)
            ?? throw new InvalidDataException("Hardware Monitor agent returned an empty response.");

        if (!string.Equals(response.ProtocolVersion, AgentProtocol.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Hardware Monitor agent returned an unsupported IPC protocol version.");
        }
        if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Hardware Monitor agent response request ID did not match.");
        }
        if (string.Equals(response.Type, AgentProtocol.Error, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(response.Error ?? "Hardware Monitor agent returned an IPC error.");
        }
        if (!string.Equals(response.Type, AgentProtocol.Status, StringComparison.Ordinal) || response.Status is null)
        {
            throw new InvalidDataException("Hardware Monitor agent returned an invalid status response.");
        }

        return response.Status;
    }

    private static async Task WriteMessageAsync(Stream stream, AgentMessage message, CancellationToken cancellationToken)
    {
        var bytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(message) + "\n");
        if (bytes.Length > AgentProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException("IPC request is too large.");
        }
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
                throw new InvalidDataException("IPC response is too large.");
            }
        }
        return StrictUtf8.GetString(buffer.ToArray());
    }
}
