using TheSpark.HardwareMonitor.Setup;

namespace TheSpark.HardwareMonitor.Setup.Tests;

public sealed class InstallerPolicyTests
{
    [Fact]
    public void PawnIO_is_installed_when_missing_or_older_than_pinned_version()
    {
        Assert.True(InstallerPolicy.ShouldInstallPawnIo(null));
        Assert.True(InstallerPolicy.ShouldInstallPawnIo(new Version(2, 1, 0, 0)));
        Assert.False(InstallerPolicy.ShouldInstallPawnIo(new Version(2, 2, 0, 0)));
        Assert.False(InstallerPolicy.ShouldInstallPawnIo(new Version(2, 3, 0, 0)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]
    [InlineData(5, false)]
    public void PawnIO_installer_accepts_only_success_or_reboot_required(int exitCode, bool expected)
    {
        Assert.Equal(expected, InstallerPolicy.IsAcceptedPawnIoExitCode(exitCode));
    }

    [Fact]
    public void PawnIO_hash_is_pinned_to_release_2_2_0()
    {
        Assert.Equal("1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032", InstallerPolicy.PawnIoSha256);
        Assert.Equal(new Version(2, 2, 0, 0), InstallerPolicy.MinimumPawnIoVersion);
    }

    [Fact]
    public void Desktop_shortcut_targets_the_stable_execution_alias()
    {
        var path = InstallerPolicy.GetExecutionAliasPath(@"C:\Users\Daniel\AppData\Local");
        Assert.Equal(@"C:\Users\Daniel\AppData\Local\Microsoft\WindowsApps\HardwareMonitor.exe", path);
    }
}
