# Hardware Monitor V1 Locked Specification

## Product identity

- Product: **Hardware Monitor**
- Publisher display name: **The Spark**
- Repository: `doonchy16-cloud/HardwareMonitor`
- Authoritative branch: `main`
- Package identity: `TheSpark.HardwareMonitor`
- Application ID: `HardwareMonitor`
- Executable: `HardwareMonitor.exe`
- Release channel: `stable`
- Product version: `1.0.0`
- MSIX version: `1.0.0.0`
- Architecture: `x64`
- Minimum OS: Windows 10 version 2004 / build 19041; Windows 11 supported.

## Technical foundation

- .NET 10 LTS, pinned to SDK 10.0.400.
- WPF desktop application, self-contained Windows x64 release.
- Provider-based sensor architecture; UI cannot depend directly on LibreHardwareMonitor types.
- LibreHardwareMonitor provider pinned to `LibreHardwareMonitorLib 0.9.7-pre726`.
- PawnIO is the privileged low-level sensor prerequisite where needed. Never silently fall back to WinRing0.
- MSIX/App Installer is the installed-app update and repair mechanism.
- Local-only telemetry; no hardware telemetry upload.
- No database in V1. Rolling graph history remains in memory.
- Sanitized bounded rotating diagnostic logs.

## Navigation and pages

Navigation is hidden by default and opened by a literal `🍔` button. It slides in as an animated drawer and closes via the burger, navigation selection, outside click, or Escape.

Exactly four top-level pages:

1. Dashboard — no tabs.
2. Sensors — CPU, GPU, Memory, Storage, Motherboard, Fans.
3. Hardware — Overview, CPU, GPU, Memory, Storage, System.
4. Settings & Diagnostics — General, Appearance, Monitoring, Diagnostics, About.

## Themes

- Light
- Dark — default on first run
- Forgey Core — deep purple, amber, lightning glow / energy accents

Theme changes apply without restart.

## Motion

V1 intentionally uses substantial animation while keeping telemetry independent of animation. Required motion includes drawer slide, burger press, page transitions, tab indicator, card entrances, interpolated values, animated gauges/bars, live heartbeat, rolling history graphs, reconnect and warning transitions, theme transitions, and Forgey Core energy/glow motion. Reduced-motion support must disable or simplify non-essential motion.

## Dashboard

Hero experience showing CPU, GPU, RAM, storage, thermal status, live freshness, primary fan data where available, uptime, animated live history, and a healthy/stale/error state. UI appears promptly while sensor discovery continues asynchronously.

## Sensors

All exposed sensors grouped by hardware type with current/min/max/average, unit, freshness, and history. Unsupported values must display `Not exposed`/`Unavailable`, never a fake zero. Multi-GPU, multi-drive, and multi-fan systems are supported.

## Hardware

Inventory of OS, system/manufacturer/model, BIOS, motherboard, CPU, GPU(s), physical/logical processor count, RAM, and storage devices. Missing metadata must degrade gracefully.

## Settings & diagnostics

General: temperature unit, startup/tray preferences.
Appearance: Light/Dark/Forgey Core and motion level.
Monitoring: default one-second polling, configurable interval/history.
Diagnostics: engine health, sensor counts, last refresh, stale readings, restart sensor engine, sanitized report, repair entrypoint.
About: Hardware Monitor, `The Spark`, semantic version, MSIX version, stable channel, Git commit.

## Performance and correctness

- Target first visible window: <500 ms on suitable hardware (must be measured before claiming compliance).
- Sensor discovery is asynchronous.
- Default polling interval: one second.
- UI target: smooth 60 FPS where display/resources permit.
- Never poll hardware on the WPF UI thread.
- Single app instance; a second launch activates the first instance.
- Every reading carries a timestamp and state. Old data must be marked stale rather than looking live.
- Sensor/backend exceptions must not crash the UI.

## Install, repair, update

- First install is a real Windows package flow.
- Desktop shortcut and Start integration are required release acceptance tests.
- MSIX package identity remains stable across versions.
- `.appinstaller` stable channel checks for updates on launch and via background task and provides repair source URIs.
- Release artifacts are signed in production; unsigned development artifacts may be generated only for CI validation.
- Release asset integrity and signature verification are required before public V1 release.

## Branding

The approved app icon is the generated dark premium rounded-square CPU/gauge/lightning design using purple and amber Forgey Core energy. No text appears in the icon. The literal burger emoji belongs only to in-app navigation.
