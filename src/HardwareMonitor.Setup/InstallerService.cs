using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace TheSpark.HardwareMonitor.Setup;

public sealed record InstallResult(bool RebootRequired);

public sealed class InstallerService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public async Task<InstallResult> InstallOrRepairAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var tempRoot = Path.Combine(Path.GetTempPath(), "TheSpark", "HardwareMonitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var rebootRequired = false;

        try
        {
            var pawnIoVersion = GetInstalledPawnIoVersion();
            if (InstallerPolicy.ShouldInstallPawnIo(pawnIoVersion))
            {
                progress.Report($"Installing PawnIO {InstallerPolicy.PawnIoVersion} hardware driver…");
                var pawnIoBytes = await Http.GetByteArrayAsync(InstallerPolicy.PawnIoDownloadUrl, cancellationToken);
                if (!InstallerPolicy.IsSha256Match(pawnIoBytes, InstallerPolicy.PawnIoSha256))
                {
                    throw new InvalidDataException("PawnIO download failed SHA-256 verification. Nothing was installed.");
                }

                var pawnIoPath = Path.Combine(tempRoot, "PawnIO_setup.exe");
                await File.WriteAllBytesAsync(pawnIoPath, pawnIoBytes, cancellationToken);
                var pawnExit = await RunProcessAsync(pawnIoPath, "-install -silent", cancellationToken);
                if (!InstallerPolicy.IsAcceptedPawnIoExitCode(pawnExit.ExitCode))
                {
                    throw new InvalidOperationException($"PawnIO installer failed with exit code {pawnExit.ExitCode}. {TrimError(pawnExit.StandardError)}");
                }

                rebootRequired = pawnExit.ExitCode == 3010;
            }
            else
            {
                progress.Report($"PawnIO {pawnIoVersion} is already ready.");
            }

            progress.Report("Downloading Hardware Monitor stable installer…");
            var appInstallerBytes = await Http.GetByteArrayAsync(InstallerPolicy.AppInstallerUrl, cancellationToken);
            ValidateAppInstaller(appInstallerBytes);
            var appInstallerPath = Path.Combine(tempRoot, "HardwareMonitor.appinstaller");
            await File.WriteAllBytesAsync(appInstallerPath, appInstallerBytes, cancellationToken);

            progress.Report("Installing Hardware Monitor and registering automatic updates…");
            var installCommand = $"Add-AppxPackage -AppInstallerFile '{PowerShellQuote(appInstallerPath)}' -ForceTargetApplicationShutdown";
            var appxExit = await RunProcessAsync("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{installCommand}\"", cancellationToken);
            if (appxExit.ExitCode != 0)
            {
                throw new InvalidOperationException($"Windows App Installer failed with exit code {appxExit.ExitCode}. {TrimError(appxExit.StandardError)}");
            }

            progress.Report("Creating Desktop shortcut…");
            await WaitForExecutionAliasAsync(cancellationToken);
            await CreateDesktopShortcutAsync(cancellationToken);

            progress.Report(rebootRequired
                ? "Installed. PawnIO requested a Windows restart for full sensor access."
                : "Installed. Hardware Monitor is ready.");

            LaunchInstalledApp();
            return new InstallResult(rebootRequired);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Version? GetInstalledPawnIoVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version))
            {
                return version;
            }
        }

        return null;
    }

    private static void ValidateAppInstaller(byte[] payload)
    {
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            var document = XDocument.Load(stream, LoadOptions.None);
            var mainPackage = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "MainPackage");
            var name = (string?)mainPackage?.Attribute("Name");
            if (!string.Equals(name, InstallerPolicy.PackageName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The downloaded App Installer file does not target Hardware Monitor.");
            }
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException("The downloaded App Installer file is not valid XML.", ex);
        }
    }

    private static async Task WaitForExecutionAliasAsync(CancellationToken cancellationToken)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasPath = InstallerPolicy.GetExecutionAliasPath(localAppData);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (File.Exists(aliasPath))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new FileNotFoundException("Windows installed Hardware Monitor, but its execution alias was not registered.", aliasPath);
    }

    private static async Task CreateDesktopShortcutAsync(CancellationToken cancellationToken)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasPath = InstallerPolicy.GetExecutionAliasPath(localAppData);
        var shortcutPath = Path.Combine(desktop, "Hardware Monitor.lnk");
        var workingDirectory = Path.GetDirectoryName(aliasPath) ?? localAppData;

        var script = "$shell = New-Object -ComObject WScript.Shell; " +
                     $"$shortcut = $shell.CreateShortcut('{PowerShellQuote(shortcutPath)}'); " +
                     $"$shortcut.TargetPath = '{PowerShellQuote(aliasPath)}'; " +
                     $"$shortcut.WorkingDirectory = '{PowerShellQuote(workingDirectory)}'; " +
                     $"$shortcut.IconLocation = '{PowerShellQuote(aliasPath)},0'; " +
                     "$shortcut.Save()";
        var result = await RunProcessAsync("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"", cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(shortcutPath))
        {
            throw new InvalidOperationException($"Desktop shortcut creation failed. {TrimError(result.StandardError)}");
        }
    }

    private static void LaunchInstalledApp()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasPath = InstallerPolicy.GetExecutionAliasPath(localAppData);
        Process.Start(new ProcessStartInfo(aliasPath) { UseShellExecute = true });
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string PowerShellQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string TrimError(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
