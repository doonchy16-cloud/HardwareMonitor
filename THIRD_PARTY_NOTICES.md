# Third-Party Notices

Hardware Monitor is developed by **The Spark**. It uses or integrates with the following third-party software.

## LibreHardwareMonitorLib

- Version pinned by this repository: `0.9.7-pre726`
- Project: LibreHardwareMonitor
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: Mozilla Public License 2.0 (MPL-2.0)
- License text: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE

Hardware Monitor consumes LibreHardwareMonitorLib as a NuGet dependency behind the `ISensorProvider` boundary. Recipients can obtain the corresponding upstream source code and MPL license from the source links above. Hardware Monitor does not remove or alter LibreHardwareMonitor's upstream license notices.

## PawnIO

- Minimum supported/pinned installer version: `2.2.0`
- Official releases: https://github.com/namazso/PawnIO.Setup/releases
- Driver source: https://github.com/namazso/PawnIO
- License: GNU General Public License v2.0 or later, with PawnIO's upstream exception text; consult the upstream source/COPYING and README for the complete terms.

PawnIO is **not bundled inside Hardware Monitor's MSIX or source tree**. `HardwareMonitor-Setup.exe` downloads the official PawnIO 2.2.0 installer directly from the upstream release location when the required driver is missing or outdated. Before execution, Hardware Monitor verifies the pinned SHA-256:

`1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

The setup bootstrap installs the normal signed PawnIO release; it does not install the unrestricted/developer driver.

## Microsoft .NET and Windows components

Hardware Monitor targets .NET 10 and WPF and uses Windows platform APIs including System.Management, Windows Installer/App Installer infrastructure, and Windows SDK packaging/signing tools. Their licensing and redistribution terms are provided by Microsoft with the respective SDK/runtime distributions.

## Test-only dependencies

The repository uses xUnit v3 and Microsoft.NET.Test.Sdk for automated tests. These are development/test dependencies and are not product features.

---

This notice describes third-party integration and source availability; it does not replace the upstream license texts or alter any third-party license terms.
