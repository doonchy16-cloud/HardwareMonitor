using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class AgentProcessLaunchPlanTests
{
    [Fact]
    public void Launch_plan_uses_packaged_agent_and_only_non_sensitive_runtime_overrides()
    {
        var executable = Path.Combine(Path.GetTempPath(), "HardwareMonitor", "HardwareMonitor.Agent.exe");
        var pipe = $"HardwareMonitor.Desktop.{Guid.NewGuid():N}";

        var plan = AgentProcessLaunchPlan.Create(executable, pipe, TimeSpan.FromMilliseconds(500));
        var startInfo = plan.CreateStartInfo();

        Assert.Equal(Path.GetFullPath(executable), startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["--pipe", pipe, "--poll-ms", "500"],
            startInfo.ArgumentList.ToArray());
        Assert.DoesNotContain("--profile-path", startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(249)]
    [InlineData(60001)]
    public void Launch_plan_rejects_poll_interval_outside_agent_contract(int milliseconds)
    {
        var executable = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Agent.exe");

        Assert.Throws<ArgumentOutOfRangeException>(() => AgentProcessLaunchPlan.Create(
            executable,
            AgentIpcProtocol.DefaultPipeName,
            TimeSpan.FromMilliseconds(milliseconds)));
    }
}