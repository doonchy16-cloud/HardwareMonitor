using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class AgentRuntimeOptionsTests
{
    [Fact]
    public void Defaults_use_local_profile_registry_and_default_pipe()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Options", Guid.NewGuid().ToString("N"));

        var options = AgentRuntimeOptions.Parse(Array.Empty<string>(), localAppData);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localAppData, "The Spark", "Hardware Monitor", "profiles.json")),
            options.ProfilePath);
        Assert.Equal(AgentIpcProtocol.DefaultPipeName, options.PipeName);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollInterval);
        Assert.False(File.Exists(options.ProfilePath));
    }

    [Fact]
    public void Explicit_overrides_are_normalized_without_touching_profile_file()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Options", Guid.NewGuid().ToString("N"));
        var profilePath = Path.Combine(localAppData, "gate", "profiles.json");
        var pipeName = $"HardwareMonitor.Gate.{Guid.NewGuid():N}";

        var options = AgentRuntimeOptions.Parse(
            ["--profile-path", profilePath, "--pipe", pipeName, "--poll-ms", "250"],
            localAppData);

        Assert.Equal(Path.GetFullPath(profilePath), options.ProfilePath);
        Assert.Equal(pipeName, options.PipeName);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.PollInterval);
        Assert.False(File.Exists(profilePath));
    }

    [Theory]
    [InlineData("--poll-ms", "0")]
    [InlineData("--poll-ms", "249")]
    [InlineData("--poll-ms", "60001")]
    [InlineData("--pipe", "   ")]
    [InlineData("--profile-path", "   ")]
    [InlineData("--unknown", "value")]
    public void Invalid_options_are_rejected(string option, string value)
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Options", Guid.NewGuid().ToString("N"));

        Assert.ThrowsAny<ArgumentException>(() => AgentRuntimeOptions.Parse([option, value], localAppData));
    }
}