using System.Security.Cryptography;

namespace TheSpark.HardwareMonitor.Setup;

public static class InstallerPolicy
{
    public static readonly Version MinimumPawnIoVersion = new(2, 2, 0, 0);

    public const string PawnIoVersion = "2.2.0";
    public const string PawnIoDownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string PawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    public const string AppInstallerUrl = "https://github.com/doonchy16-cloud/HardwareMonitor/releases/latest/download/HardwareMonitor.appinstaller";
    public const string PackageName = "TheSpark.HardwareMonitor";
    public const string ExecutionAlias = "HardwareMonitor.exe";

    public static bool ShouldInstallPawnIo(Version? installedVersion) =>
        installedVersion is null || installedVersion < MinimumPawnIoVersion;

    public static bool IsAcceptedPawnIoExitCode(int exitCode) => exitCode is 0 or 3010;

    public static bool IsSha256Match(ReadOnlySpan<byte> payload, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return false;
        }

        var actual = Convert.ToHexString(SHA256.HashData(payload));
        return string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetExecutionAliasPath(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(localApplicationData, "Microsoft", "WindowsApps", ExecutionAlias);
    }
}
