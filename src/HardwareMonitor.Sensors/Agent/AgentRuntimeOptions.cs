using System.Globalization;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed record AgentRuntimeOptions(
    string ProfilePath,
    string PipeName,
    TimeSpan PollInterval,
    string? BridgeRoot,
    string TelemetrySequencePath)
{
    private const int MinimumPollMilliseconds = 250;
    private const int MaximumPollMilliseconds = 60_000;
    private const int DefaultPollMilliseconds = 1_000;

    public static AgentRuntimeOptions Parse(IReadOnlyList<string> args, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new ArgumentException("Local application data path cannot be blank.", nameof(localAppData));
        }

        var appDataRoot = Path.Combine(localAppData.Trim(), "The Spark", "Hardware Monitor");
        var profilePath = Path.Combine(appDataRoot, "profiles.json");
        var pipeName = AgentIpcProtocol.DefaultPipeName;
        var pollMilliseconds = DefaultPollMilliseconds;

        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (string.IsNullOrWhiteSpace(option))
            {
                throw new ArgumentException("Agent option cannot be blank.", nameof(args));
            }

            if (index + 1 >= args.Count)
            {
                throw new ArgumentException($"Agent option '{option}' requires a value.", nameof(args));
            }

            var value = args[++index];
            switch (option)
            {
                case "--profile-path":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("Profile path cannot be blank.", nameof(args));
                    }

                    profilePath = value.Trim();
                    break;

                case "--pipe":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("Pipe name cannot be blank.", nameof(args));
                    }

                    pipeName = value.Trim();
                    break;

                case "--poll-ms":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pollMilliseconds)
                        || pollMilliseconds < MinimumPollMilliseconds
                        || pollMilliseconds > MaximumPollMilliseconds)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(args),
                            $"Polling interval must be between {MinimumPollMilliseconds} and {MaximumPollMilliseconds} milliseconds.");
                    }
                    break;

                default:
                    throw new ArgumentException($"Unknown agent option '{option}'.", nameof(args));
            }
        }

        return new AgentRuntimeOptions(
            Path.GetFullPath(profilePath),
            pipeName,
            TimeSpan.FromMilliseconds(pollMilliseconds),
            null,
            Path.GetFullPath(Path.Combine(appDataRoot, "gateway-telemetry-sequence.json")));
    }
}