# Hardware Monitor Private Multi-Profile Monitoring Design

**Status:** LOCKED

## Goal

Evolve Hardware Monitor from a local Windows telemetry utility into a polished private, cross-platform monitoring platform that can monitor Windows and Android devices and display their current health from any authorized viewer, including a phone, without requiring public Store publication or paid code signing.

## Locked product rules

1. No profile names, device names, roles, or operating-system identities are hard-coded.
2. First launch may contain zero user profiles; profiles are created and configured in the app.
3. A profile is a logical identity, not a physical device.
4. One physical device may have multiple profiles.
5. Profiles use composable capabilities instead of one rigid role.
6. Viewer profiles support `AllProfiles`, `SelectedProfiles`, or no viewer scope.
7. `AllProfiles` dynamically includes every present and future authorized profile.
8. Every practical user-facing monitoring option is configurable in the app.
9. Rendering is status-first and capability-driven.
10. Unsupported telemetry is omitted, never represented by fake zeroes or placeholder rows.
11. `STALE` is distinct from `OFFLINE`.
12. `OFFLINE` renders the state and last-seen time only; it does not render a dead metric grid.
13. Per-profile stale/offline thresholds, thermal thresholds, sensor visibility, dashboard layout, and alert policy are configurable.
14. Windows, Android, Web/PWA, and future Linux/macOS clients sit beneath one normalized telemetry model.
15. Android is a first-class viewer and monitored device; it publishes only values actually exposed by Android/device APIs.
16. Local hardware monitoring continues if the Gateway or Internet is unavailable.
17. Remote access uses authenticated private transport; no raw unauthenticated PC port is exposed.
18. The initial owner-only deployment does not depend on Microsoft Store publication, a public AppInstaller feed, or a paid production signing certificate.
19. Existing V1 sensor/core/platform/diagnostic foundations are retained and extended.
20. No component may claim success from QUEUED/RUNNING workflow state; terminal evidence is required.

## Existing V1 foundation

The current solution already contains:

- `HardwareMonitor.App` — WPF shell, Dashboard, Sensors, Hardware, Settings & Diagnostics, animated drawer, themes, motion, settings.
- `HardwareMonitor.Core` — immutable hardware/sensor models, `TemperatureClassifier`, `RollingSeries`.
- `HardwareMonitor.Sensors` — `ISensorProvider`, `LibreHardwareMonitorProvider`, snapshot normalization, asynchronous `HardwareMonitorService`.
- `HardwareMonitor.Platform.Windows` — Windows hardware/system inventory.
- `HardwareMonitor.Diagnostics` — bounded rotating logs and sanitization.
- setup/bootstrap and Windows packaging projects.

The new design must layer profile/device/remote semantics above these boundaries instead of moving hardware access into UI code.

## Profile model

A profile contains at minimum:

```text
ProfileId: UUID
Name: user-created
Icon: optional user-configurable value
Description: optional
Enabled: bool
Capabilities: set<ProfileCapability>
DeviceBinding: optional DeviceId
ViewerScope: None | AllProfiles | SelectedProfiles
VisibleProfileIds: set<ProfileId>
DashboardConfiguration
SensorVisibilityPolicy
ThermalThresholdPolicy
FreshnessPolicy
AlertPolicy
RemoteAccessPolicy
CreatedAt
UpdatedAt
Revision
```

Initial capabilities include:

```text
ViewProfiles
PublishHardwareTelemetry
PublishDevicePresence
PublishLimitedClientTelemetry
TrainingMode
ManageProfiles
ManageDevices
ManageAlerts
ManageRemoteAccess
ReceiveNotifications
ViewDiagnostics
ViewHistory
```

Capabilities may be combined freely. A phone may be both Viewer and Monitor. A Windows training machine may publish telemetry and also view other profiles.

## Device model

A device record represents a physical/runtime endpoint and is separate from profiles.

```text
DeviceId
UserAlias
Platform
Architecture
AgentVersion
InstalledSha
Transport
OnlineState
LastHeartbeatAt
LastTelemetryAt
SupportedCapabilities
SensorCapabilities
RegistrationState
```

Device enrollment is user-driven. Multiple profiles may bind to one `DeviceId`.

## Normalized platform adapters

Shared layers consume a normalized platform contract rather than Windows-specific sensor types.

Conceptual contract:

```text
IPlatformTelemetryAgent
  GetDeviceIdentity()
  GetCapabilities()
  GetInventory()
  GetTelemetry()
  GetPresence()
  GetHealth()
  GetPlatformVersion()
  PublishNormalizedSnapshot()
```

### Windows

Windows uses the existing LibreHardwareMonitor and Windows inventory stack and may expose CPU/GPU/hotspot/fans/power/storage/RAM/clocks/temperatures where available.

### Android

Android is first-class. When actually exposed, it may publish battery percentage, charging state, battery temperature, battery health, memory, storage, network state, device model, Android version, app version, uptime/session state, thermal/throttling state, and CPU/load information. It must never fabricate CPU/GPU temperatures, fan RPM, power, or any unsupported metric.

### Web/PWA

A PWA can be an authenticated viewer and may publish only browser-exposed presence/limited client telemetry. It must not pretend to be a native hardware agent.

## Telemetry routing

The existing device `HardwareSnapshot` remains the raw normalized Windows telemetry frame. A new Profile Telemetry Router maps one device snapshot into zero or more `ProfileTelemetrySnapshot` objects based on profiles bound to that device.

Each profile view applies:

- sensor visibility,
- per-profile thermal thresholds,
- units/presentation policy,
- freshness policy,
- optional TrainingMode emphasis,
- alert policy.

`ProfileTelemetrySnapshot` contains only viewer-safe fields and includes ProfileId, DeviceId, capture/receive timestamps, primary/secondary status, thermal summary, visible metrics, source freshness, and active alerts.

## Presence and freshness

Presence is independent of individual sensor availability.

Core state dimensions are:

```text
Connectivity: Online | Stale | Offline
Activity: Idle | Training | Unknown
Health: Healthy | Degraded | Error
Thermal: Normal | Warm | Hot | Critical | Unavailable
```

The UI may render combinations such as `TRAINING • DEGRADED`.

Per-profile freshness defaults are configurable. State transitions are:

```text
fresh telemetry -> ONLINE/TRAINING/IDLE
age > staleAfter -> STALE
age > offlineAfter -> OFFLINE
fresh telemetry returns -> current live state
```

STALE may show clearly marked last-known readings. OFFLINE shows only the status and last-seen time.

## Status-first renderer

Rendering asks:

1. What capabilities does this profile/device expose?
2. What state is it in?
3. Which telemetry values are valid now?

Rules:

- ONLINE/TRAINING/IDLE: show configured live metrics.
- STALE: show state, age, and last-known valid readings marked historical.
- OFFLINE: state + last seen only.
- DEGRADED: show only remaining valid telemetry and degradation reason.
- ERROR: actionable error + last healthy timestamp where known.

## Background agent and local IPC

Hardware monitoring must not depend on the WPF window remaining open.

A background agent will:

```text
load validated local config
initialize sensors
collect telemetry
classify health/thermal state
run local alerts
publish presence
publish remote telemetry
sync profile/device configuration
maintain bounded local history and diagnostics
```

The desktop UI communicates with the agent over local-only IPC and does not duplicate hardware polling.

## Profile registry and offline cache

The authoritative profile model is a central private Profile Registry with validated local caches on clients/agents.

- Profile updates use revision numbers.
- Clients pull changes by revision.
- Local monitoring continues from the last validated cache during remote outages.
- Writes are atomic.
- Conflicting offline edits must not silently overwrite newer authoritative revisions.
- Secrets are not stored in general profile JSON.

## Private Gateway integration

Doonchy Bridge/Gateway is reused as the private remote transport and authority boundary, but Hardware Monitor uses purpose-built telemetry/profile APIs rather than arbitrary shell execution as its normal data path.

Conceptual services:

```text
HardwareMonitor.Registry
HardwareMonitor.Telemetry
HardwareMonitor.Presence
HardwareMonitor.Viewer
HardwareMonitor.Alerts
```

Gateway responsibilities:

- authenticate device/viewer identity,
- validate schema/version,
- enforce payload/rate limits,
- sanitize logs,
- maintain latest telemetry and presence,
- derive remote freshness,
- serve authorized viewer reads,
- apply authorized profile/device mutations,
- provide a live update channel (SSE or WebSocket),
- optionally maintain bounded short history.

## Phone viewer

The first remote viewer is a responsive authenticated phone web/PWA dashboard.

It supports:

- profile pairing,
- active viewer profile,
- dynamic All Profiles,
- status-first cards,
- critical/hot/training/stale/offline sorting,
- profile detail,
- alerts,
- live updates with polling fallback,
- profile management only when granted capabilities.

A phone self-profile may publish presence and limited browser/Android telemetry actually exposed by the platform.

## Alerts

Alert types include thermal warning/hot/critical, stale, offline, degraded sensors, sensor-engine error, and recovery. Alerts are deduplicated, policy-controlled per profile, and may be shown locally and remotely. Recovery notifications are configurable.

## Persistence boundaries

Keep stores separate by purpose:

```text
Local app settings
Local profile/config cache
Local agent state
Local rolling telemetry
Local diagnostics
Central profile registry
Gateway current-state telemetry
Gateway presence
Gateway alert state
Optional bounded history
Protected auth/session secret storage
```

## Security rules

- No unauthenticated inbound public PC port.
- Device and viewer access is authenticated.
- Pairing credentials are short-lived.
- Profile mutation requires explicit capability.
- Telemetry is sanitized and bounded.
- No arbitrary shell command is exposed through Hardware Monitor telemetry APIs.
- No credentials/tokens/private authority data in profile payloads, telemetry, or copied diagnostics.
- Device/viewer revoke is supported.
- Remote visibility can be disabled per profile.

## Verification and acceptance

The first useful end-to-end target is Windows/Forgey-PC -> private Gateway -> phone PWA. Android native monitoring follows the same contracts and may be added without changing profile/viewer semantics.

Terminal acceptance requires proof of all of the following:

1. Profiles are created in-app; required example names do not exist as hard-coded defaults.
2. One device can bind multiple profiles.
3. AllProfiles dynamically picks up a newly created profile.
4. Real physical CPU/GPU telemetry is captured.
5. Per-profile thermal/freshness policy changes rendered state.
6. STALE transition is demonstrated.
7. OFFLINE transition is demonstrated and renders no placeholder metric grid.
8. DEGRADED preserves valid telemetry and omits unavailable values.
9. Gateway receives a real current telemetry value.
10. Phone-sized viewer reads and renders the same value.
11. Local monitoring continues through Gateway failure using cached configuration.
12. No credentials/tokens/private authority data appear in telemetry or diagnostics.
13. Hosted CI passes.
14. Real Hardware Gate passes on physical Windows hardware.
