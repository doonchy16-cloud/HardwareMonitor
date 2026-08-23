using System.Diagnostics;
using System.Globalization;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed record AgentProcessLaunchPlan
{
    private const int MinimumPollMilliseconds = 250;
    private const int MaximumPollMilliseconds = 60_000;

    private AgentProcessLaunchPlan(string executablePath, string pipeName, int pollMilliseconds)
    {
        ExecutablePath = executablePath;
        PipeName = pipeName;
        PollMilliseconds = pollMilliseconds;
    }

    public string ExecutablePath { get; }

    public string PipeName { get; }

    public int PollMilliseconds { get; }

    public static AgentProcessLaunchPlan Create(string executablePath, string pipeName, TimeSpan pollInterval)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Agent executable path cannot be blank.", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Agent pipe name cannot be blank.", nameof(pipeName));
        }

        if (pollInterval < TimeSpan.FromMilliseconds(MinimumPollMilliseconds)
            || pollInterval > TimeSpan.FromMilliseconds(MaximumPollMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        var pollMilliseconds = checked((int)pollInterval.TotalMilliseconds);
        return new AgentProcessLaunchPlan(
            Path.GetFullPath(executablePath.Trim()),
            pipeName.Trim(),
            pollMilliseconds);
    }

    public ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(PipeName);
        startInfo.ArgumentList.Add("--poll-ms");
        startInfo.ArgumentList.Add(PollMilliseconds.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }
}