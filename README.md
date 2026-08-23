# Hardware Monitor

**Hardware Monitor** is a fast, animated Windows hardware telemetry desktop application by **The Spark**.

V1 targets **Windows 10 build 19041+ and Windows 11, x64**, using **.NET 10 LTS + WPF**. The authoritative production branch is `main`.

The locked product specification is in [`docs/specs/v1-locked-spec.md`](docs/specs/v1-locked-spec.md).

## V1 experience

Hardware Monitor is deliberately a focused desktop utility rather than a large system-management suite.

- **Dashboard** — CPU/GPU/RAM/storage overview, thermal state, live sparklines and animated utilization.
- **Sensors** — CPU, GPU, Memory, Storage, Motherboard and Fans tabs with timestamped sensor readings.
- **Hardware** — Overview, CPU, GPU, Memory, Storage and System inventory tabs.
- **Settings & Diagnostics** — General, Appearance, Monitoring, Diagnostics and About.
- Literal animated **🍔** navigation drawer.
- **Light**, **Dark**, and **Forgey Core** themes.
- Full / Reduced / Off motion levels.
- Celsius / Fahrenheit and 500 ms / 1 s / 2 s polling options.
- Missing hardware values are reported as unavailable / not exposed; they are never replaced with fake zeroes.
- Multiple GPUs, storage devices and other enumerated devices remain distinct.
- Telemetry stays local to the computer.

## Architecture

```text
HardwareMonitor.App (WPF)
        │
        ├── HardwareMonitor.Core
        ├── HardwareMonitor.Diagnostics
        ├── HardwareMonitor.Platform.Windows
        └── HardwareMonitor.Sensors
                │
                └── ISensorProvider
                     └── LibreHardwareMonitorProvider
```

The UI never owns low-level hardware access. Sensor collection runs asynchronously and produces immutable timestamped snapshots. This keeps telemetry, animation and presentation independently testable and lets the low-level provider be replaced without rewriting the app.

## Hardware access

Hardware Monitor uses `LibreHardwareMonitorLib` behind the provider boundary. Some low-level sensors require **PawnIO**. The initial setup bootstrap installs the normal signed PawnIO 2.2.0 release only when it is missing or older than the pinned minimum.

The bootstrap:

1. downloads PawnIO from the official upstream release,
2. verifies its pinned SHA-256,
3. requests administrator approval **only for the driver installation**,
4. installs Hardware Monitor as the invoking Windows user,
5. registers the App Installer update/repair relationship,
6. creates the Desktop shortcut using the stable execution alias,
7. launches Hardware Monitor.

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for upstream source and license information.

## Windows installation and lifecycle

Production releases are designed around:

- `HardwareMonitor-Setup.exe` — initial install/repair bootstrap.
- `HardwareMonitor-<version>-x64.msix` — signed Windows application package.
- `HardwareMonitor.appinstaller` — stable update/repair policy.

Once installed, users do **not** manually download every new version. The App Installer association checks for updates on launch and also registers background update behavior. The app's **Check for Updates** action relaunches through the stable execution alias, causing Windows to perform the registered launch check.

The App Installer file also contains a repair URI. **Repair Installation** routes directly to Hardware Monitor's own Windows Advanced Options page so Windows can perform its native MSIX repair while preserving app data.

## Build locally

Requirements for a developer build:

- Windows 10 build 19041+ or Windows 11
- .NET SDK `10.0.400` (pinned in `global.json`)
- Windows SDK when building MSIX packages

```powershell
dotnet restore HardwareMonitor.sln
dotnet build HardwareMonitor.sln --configuration Release --no-restore
dotnet test --solution HardwareMonitor.sln --configuration Release --no-build --minimum-expected-tests 1
```

To publish a self-contained x64 app:

```powershell
dotnet publish src/HardwareMonitor.App/HardwareMonitor.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true
```

To create an unsigned **verification** MSIX on a Windows machine with the SDK installed:

```powershell
./packaging/Build-Msix.ps1 -Version 1.0.0.0 -Publisher 'CN=The Spark' -ReleaseTag v1.0.0
```

An unsigned package is a verification artifact only and is **not** a production release.

## Continuous verification

Two independent Windows gates are used:

1. **Hosted CI** — restores, builds and executes the complete automated test suite on GitHub-hosted Windows.
2. **Real Hardware Gate** — runs only on trusted direct pushes to `main` / `build/v1` using the repository's self-hosted Windows x64 runner. It builds/tests again, probes actual hardware and temperature sensors, publishes the self-contained executables, and validates MSIX packaging with Windows SDK tooling.

The self-hosted workflow is intentionally not triggered by public pull-request code.

## Stable releases and updates

A production stable release is created from a semantic version tag such as `v1.0.0`, and the tag must point to a commit already contained in authoritative `main`.

The release workflow verifies that:

- the Git tag matches `Directory.Build.props` `VersionPrefix`,
- all tests pass,
- a production signing certificate is available,
- the certificate has a private key and adequate validity,
- the certificate subject becomes the MSIX publisher identity,
- the MSIX and setup bootstrap are SHA-256 signed and RFC 3161 timestamped,
- SignTool verifies both signatures before release publication,
- SHA-256 hashes are generated for the release artifacts.

Required repository Actions secrets:

- `WINDOWS_SIGNING_PFX_BASE64`
- `WINDOWS_SIGNING_PFX_PASSWORD`

The first production signing identity becomes part of the installed package identity and must be treated as stable for future updates.

## Version authority

`Directory.Build.props` is the single product-version authority.

For V1:

```text
Product version: 1.0.0
MSIX version:    1.0.0.0
Git tag:         v1.0.0
Channel:         stable
Package:         TheSpark.HardwareMonitor
App ID:          HardwareMonitor
Executable:      HardwareMonitor.exe
```

## Privacy and diagnostics

Hardware telemetry is processed locally. Diagnostic logging is bounded and sanitized. Diagnostic reports must not contain credentials, tokens or unnecessary unique hardware identifiers.

## Repository policy

- `main` is production authority.
- Do not call a build healthy merely because a workflow is queued or running.
- Release only from terminal successful build/test/package gates.
- Do not publish an unsigned MSIX as a production release.
