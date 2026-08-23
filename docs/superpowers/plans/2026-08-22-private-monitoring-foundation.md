# Private Multi-Profile Monitoring Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Hardware Monitor side of the private multi-profile platform: profile/device domain models, capability-driven status/freshness logic, profile telemetry routing, persistent local profile configuration, background-agent contracts, remote-safe telemetry contracts, and the desktop Profiles UI needed to configure everything without hard-coded profiles.

**Architecture:** Keep physical sensor collection behind the existing `HardwareMonitor.Sensors` boundary. Add platform-neutral profile/device models to `HardwareMonitor.Core`, route existing `HardwareSnapshot` frames into profile-specific views, persist local configuration atomically, and expose a clean remote protocol boundary that the Bridge/Gateway companion implementation can consume. The first useful transport target is Windows -> Gateway -> phone PWA; Android later implements the same platform-neutral contracts.

**Tech Stack:** .NET 10 LTS, C# 13/.NET SDK 10.0.400, WPF, xUnit, System.Text.Json, existing LibreHardwareMonitor integration.

**Spec:** `docs/superpowers/specs/2026-08-22-private-multiprofile-monitoring-design.md`

## Global Constraints

- `main` is production authority; implementation occurs on `feature/private-multiprofile-v2` until terminal verification is complete.
- No user profile/device names or roles are hard-coded.
- One physical device may bind multiple profiles.
- Profiles use composable capabilities.
- `AllProfiles` dynamically includes future authorized profiles.
- STALE and OFFLINE are distinct.
- OFFLINE renders status + last-seen only; no placeholder metric grid.
- Unsupported telemetry is omitted, never fabricated.
- All practical thresholds and policies are user-configurable in-app.
- Windows remains the first complete agent; shared domain/contracts are platform-neutral and Android-ready.
- Secrets do not live in general profile/config/telemetry JSON.
- Tests are written first for every behavior change.
- QUEUED/RUNNING CI states are never accepted as success.

---

### Task 1: Profile, Capability, Device, and Freshness Domain

**Files:**
- Create: `src/HardwareMonitor.Core/Profiles/ProfileCapability.cs`
- Create: `src/HardwareMonitor.Core/Profiles/ViewerScope.cs`
- Create: `src/HardwareMonitor.Core/Profiles/FreshnessPolicy.cs`
- Create: `src/HardwareMonitor.Core/Profiles/ThermalThresholdPolicy.cs`
- Create: `src/HardwareMonitor.Core/Profiles/HardwareProfile.cs`
- Create: `src/HardwareMonitor.Core/Devices/DevicePlatform.cs`
- Create: `src/HardwareMonitor.Core/Devices/DeviceRecord.cs`
- Create: `src/HardwareMonitor.Core/Status/ConnectivityState.cs`
- Create: `src/HardwareMonitor.Core/Status/ActivityState.cs`
- Create: `src/HardwareMonitor.Core/Status/HealthState.cs`
- Create: `src/HardwareMonitor.Core/Status/ProfileStatus.cs`
- Create: `src/HardwareMonitor.Core/Status/ProfileStatusEvaluator.cs`
- Test: `tests/HardwareMonitor.Core.Tests/ProfileDomainTests.cs`
- Test: `tests/HardwareMonitor.Core.Tests/ProfileStatusEvaluatorTests.cs`

**Interfaces:**
- Produces: `HardwareProfile`, `DeviceRecord`, `FreshnessPolicy`, `ThermalThresholdPolicy`, and `ProfileStatusEvaluator.Evaluate(DateTimeOffset now, DateTimeOffset? lastTelemetryAt, FreshnessPolicy policy, ActivityState activity, HealthState health)`.

- [ ] **Step 1: Write failing profile-domain tests**

Test exact locked invariants: generated/user-supplied profile ID is independent of device alias; capabilities can combine Viewer + Publisher + Training; `AllProfiles` does not require selected IDs; multiple `HardwareProfile` instances may share one DeviceId.

- [ ] **Step 2: Run Core tests and require RED**

Run: `dotnet test tests/HardwareMonitor.Core.Tests/HardwareMonitor.Core.Tests.csproj --configuration Release`

Expected: compile/test failure because new profile types do not exist.

- [ ] **Step 3: Implement minimal immutable domain records/enums**

Use focused records/enums with validation in constructors/factories. `FreshnessPolicy` must reject non-positive stale thresholds and must require `OfflineAfter > StaleAfter`.

- [ ] **Step 4: Write failing freshness/status tests**

Cover fresh -> Online, `age > StaleAfter` -> Stale, `age > OfflineAfter` -> Offline, health degradation modifier, and return-to-online after a new timestamp.

- [ ] **Step 5: Implement `ProfileStatusEvaluator` and run tests GREEN**

Run the Core test project and require zero failures.

- [ ] **Step 6: Commit**

Commit message: `feat: add profile device and freshness domain`

---

### Task 2: Capability-Driven Platform Telemetry Contracts

**Files:**
- Create: `src/HardwareMonitor.Core/Platforms/PlatformTelemetryCapability.cs`
- Create: `src/HardwareMonitor.Core/Platforms/PlatformCapabilities.cs`
- Create: `src/HardwareMonitor.Core/Platforms/NormalizedMetric.cs`
- Create: `src/HardwareMonitor.Core/Platforms/NormalizedTelemetrySnapshot.cs`
- Create: `src/HardwareMonitor.Core/Platforms/IPlatformTelemetryAdapter.cs`
- Test: `tests/HardwareMonitor.Core.Tests/PlatformTelemetryContractTests.cs`

**Interfaces:**
- Produces: platform-neutral capability metadata and normalized snapshots used by Windows and future Android agents.

- [ ] Write tests that prove a platform may omit GPU temperature entirely while remaining healthy and that capability absence is not represented by zero/placeholder metrics.
- [ ] Run tests RED.
- [ ] Implement minimal platform-neutral contracts.
- [ ] Run Core tests GREEN.
- [ ] Commit: `feat: add cross platform telemetry contracts`.

---

### Task 3: Profile Telemetry Router

**Files:**
- Create: `src/HardwareMonitor.Core/Profiles/ProfileTelemetrySnapshot.cs`
- Create: `src/HardwareMonitor.Core/Profiles/ProfileTelemetryRouter.cs`
- Create: `src/HardwareMonitor.Core/Profiles/SensorVisibilityPolicy.cs`
- Test: `tests/HardwareMonitor.Core.Tests/ProfileTelemetryRouterTests.cs`

**Interfaces:**
- Consumes: existing `HardwareSnapshot`, `HardwareProfile` collection.
- Produces: `IReadOnlyList<ProfileTelemetrySnapshot> Route(HardwareSnapshot snapshot, IReadOnlyCollection<HardwareProfile> profiles, DateTimeOffset receivedAt)`.

- [ ] Write failing tests for two profiles bound to the same device yielding two independent routed views.
- [ ] Write failing test that an unbound viewer profile produces no hardware telemetry snapshot.
- [ ] Write failing test that unavailable sensors are omitted from visible metrics.
- [ ] Implement router and per-profile visibility filtering.
- [ ] Run Core tests GREEN.
- [ ] Commit: `feat: route device telemetry through profiles`.

---

### Task 4: Local Profile Repository and Atomic Cache

**Files:**
- Create: `src/HardwareMonitor.Core/Profiles/IProfileRepository.cs`
- Create: `src/HardwareMonitor.Core/Profiles/ProfileRegistrySnapshot.cs`
- Create: `src/HardwareMonitor.App/Services/LocalProfileRepository.cs`
- Test: `tests/HardwareMonitor.Core.Tests/ProfileRegistrySnapshotTests.cs`
- Create test project if needed: `tests/HardwareMonitor.App.Tests/HardwareMonitor.App.Tests.csproj`
- Test: `tests/HardwareMonitor.App.Tests/LocalProfileRepositoryTests.cs`
- Modify: `HardwareMonitor.sln`

**Interfaces:**
- Produces: `LoadAsync`, `SaveAsync`, revisioned profile registry snapshots.

- [ ] Write failing serialization/round-trip tests with zero profiles and with multiple profiles sharing a DeviceId.
- [ ] Write failing atomic-save test using a temp directory.
- [ ] Implement deterministic JSON serialization and replace-via-temp-file persistence.
- [ ] Corrupt-cache behavior: return an explicit load error result; do not silently replace authoritative config with defaults.
- [ ] Run all new tests GREEN.
- [ ] Commit: `feat: persist profile registry atomically`.

---

### Task 5: Dynamic AllProfiles Resolver

**Files:**
- Create: `src/HardwareMonitor.Core/Profiles/ProfileVisibilityResolver.cs`
- Test: `tests/HardwareMonitor.Core.Tests/ProfileVisibilityResolverTests.cs`

**Interfaces:**
- Produces: `ResolveVisibleProfiles(HardwareProfile viewer, IReadOnlyCollection<HardwareProfile> allProfiles)`.

- [ ] Test `AllProfiles` returns all enabled/authorized profiles including a profile added after the viewer was created.
- [ ] Test `SelectedProfiles` returns only selected IDs.
- [ ] Test disabled profiles are excluded unless management UI explicitly requests them.
- [ ] Implement minimal resolver.
- [ ] Run Core tests GREEN.
- [ ] Commit: `feat: resolve dynamic all profiles viewers`.

---

### Task 6: Status-First Presentation Model

**Files:**
- Create: `src/HardwareMonitor.App/ViewModels/ProfileCardViewModel.cs`
- Create: `src/HardwareMonitor.App/Services/ProfileCardPresenter.cs`
- Test: `tests/HardwareMonitor.App.Tests/ProfileCardPresenterTests.cs`

**Interfaces:**
- Consumes: `ProfileTelemetrySnapshot`, `ProfileStatus`.
- Produces: card model with `ShowMetrics`, `StatusText`, `LastSeenText`, and only valid metric rows.

- [ ] Test OFFLINE produces `ShowMetrics=false` and zero metric rows.
- [ ] Test STALE retains last-known metrics but marks them historical.
- [ ] Test DEGRADED omits unavailable metrics while retaining available metrics.
- [ ] Test ONLINE/TRAINING shows live configured metrics.
- [ ] Implement presenter without hard-coded device/profile names.
- [ ] Run App tests GREEN.
- [ ] Commit: `feat: add status first profile presentation`.

---

### Task 7: Profiles Management UI

**Files:**
- Create: `src/HardwareMonitor.App/Pages/ProfilesPage.xaml`
- Create: `src/HardwareMonitor.App/Pages/ProfilesPage.xaml.cs`
- Create: `src/HardwareMonitor.App/ViewModels/ProfilesViewModel.cs`
- Create: `src/HardwareMonitor.App/ViewModels/ProfileEditorViewModel.cs`
- Modify: `src/HardwareMonitor.App/MainWindow.xaml`
- Modify: `src/HardwareMonitor.App/MainWindow.xaml.cs`
- Modify: `src/HardwareMonitor.App/App.xaml.cs`
- Test: `tests/HardwareMonitor.App.Tests/ProfilesViewModelTests.cs`

**Interfaces:**
- Uses: local profile repository and domain model.

- [ ] Test first launch with an empty repository shows zero profiles and an Add Profile action.
- [ ] Test create/edit/delete/enable/disable without any built-in names.
- [ ] Test capability toggles and ViewerScope editing.
- [ ] Test per-profile stale/offline values validate through `FreshnessPolicy`.
- [ ] Implement Profiles page using existing visual language/themes/motion.
- [ ] Add Profiles to 🍔 navigation without removing existing top-level pages.
- [ ] Run App tests and solution build GREEN.
- [ ] Commit: `feat: add configurable profiles ui`.

---

### Task 8: Background Agent Process and Health Contract

**Files:**
- Create: `src/HardwareMonitor.Agent/HardwareMonitor.Agent.csproj`
- Create: `src/HardwareMonitor.Agent/Program.cs`
- Create: `src/HardwareMonitor.Agent/AgentHost.cs`
- Create: `src/HardwareMonitor.Agent/AgentHealthSnapshot.cs`
- Create: `tests/HardwareMonitor.Agent.Tests/HardwareMonitor.Agent.Tests.csproj`
- Create: `tests/HardwareMonitor.Agent.Tests/AgentHostTests.cs`
- Modify: `HardwareMonitor.sln`

**Interfaces:**
- Consumes: `HardwareMonitor.Sensors`, profile repository/cache, telemetry router.
- Produces: latest local profile telemetry and agent health independent of WPF process lifetime.

- [ ] Write test proving agent collection loop can start/stop with a fake `ISensorProvider` without WPF.
- [ ] Write test proving sensor/provider failure sets agent health to Error/Degraded but process loop remains controlled.
- [ ] Implement minimal hosted loop with cancellation and bounded exception handling.
- [ ] Run Agent tests GREEN.
- [ ] Commit: `feat: add background hardware monitor agent`.

---

### Task 9: Local IPC Between Desktop and Agent

**Files:**
- Create: `src/HardwareMonitor.Core/Ipc/AgentMessage.cs`
- Create: `src/HardwareMonitor.Agent/LocalIpcServer.cs`
- Create: `src/HardwareMonitor.App/Services/AgentIpcClient.cs`
- Test: `tests/HardwareMonitor.Agent.Tests/LocalIpcServerTests.cs`
- Test: `tests/HardwareMonitor.App.Tests/AgentIpcClientTests.cs`

**Interfaces:**
- JSON line/protocol over local named pipe with versioned message envelope.

- [ ] Test current-status request/response.
- [ ] Test malformed/oversized local message rejection.
- [ ] Test desktop disconnect does not stop monitoring agent.
- [ ] Implement local-only named-pipe protocol.
- [ ] Run tests GREEN.
- [ ] Commit: `feat: connect desktop to monitoring agent`.

---

### Task 10: Remote-Safe Protocol Models and Publisher Client

**Files:**
- Create: `src/HardwareMonitor.Core/Remote/TelemetryEnvelope.cs`
- Create: `src/HardwareMonitor.Core/Remote/PresenceEnvelope.cs`
- Create: `src/HardwareMonitor.Core/Remote/ProfileRegistryEnvelope.cs`
- Create: `src/HardwareMonitor.Agent/RemoteTelemetryPublisher.cs`
- Create: `src/HardwareMonitor.Agent/ProfileSyncClient.cs`
- Test: `tests/HardwareMonitor.Core.Tests/RemoteEnvelopeTests.cs`
- Test: `tests/HardwareMonitor.Agent.Tests/RemoteTelemetryPublisherTests.cs`

**Interfaces:**
- Versioned JSON contracts; endpoint URI and protected credential source are injected configuration.

- [ ] Test envelopes do not serialize raw credentials or authority fields.
- [ ] Test obsolete high-frequency frames are dropped/coalesced rather than queued without bound.
- [ ] Test Gateway failure leaves local monitoring active and uses retry/backoff.
- [ ] Implement remote clients against an HTTP abstraction so tests use fake handlers.
- [ ] Run tests GREEN.
- [ ] Commit: `feat: add private remote telemetry client`.

---

### Task 11: Local Alerts and Configurable Thermal Policies

**Files:**
- Create: `src/HardwareMonitor.Core/Alerts/AlertKind.cs`
- Create: `src/HardwareMonitor.Core/Alerts/AlertEvent.cs`
- Create: `src/HardwareMonitor.Core/Alerts/AlertEngine.cs`
- Test: `tests/HardwareMonitor.Core.Tests/AlertEngineTests.cs`
- Modify: profile editor UI to configure thermal/freshness alerts.

**Interfaces:**
- Consumes current and prior profile status/thermal state; produces deduplicated alert/recovery events.

- [ ] Test Warm/Hot/Critical threshold crossings.
- [ ] Test stale/offline alerts.
- [ ] Test duplicate frames do not spam duplicate active alerts.
- [ ] Test recovery event.
- [ ] Implement alert engine and UI settings.
- [ ] Run tests GREEN.
- [ ] Commit: `feat: add per profile monitoring alerts`.

---

### Task 12: Verification and Real-Hardware Integration Gate

**Files:**
- Modify: `.github/workflows/ci.yml` as needed for new projects.
- Modify: real-hardware workflow to prove agent/profile routing and remote-contract generation without secrets.
- Extend: `tools/HardwareMonitor.HardwareSmoke` or equivalent existing smoke tool.

- [ ] Hosted CI: restore/build/test every project.
- [ ] Real hardware: capture actual hardware snapshot and at least one real temperature sensor where exposed.
- [ ] Route the physical snapshot through a dynamically created test profile at runtime.
- [ ] Verify OFFLINE/STALE tests remain unit-tested; never deliberately power off the runner for CI.
- [ ] Verify generated telemetry envelope contains the same real sensor value and no secret fields.
- [ ] Require terminal successful hosted and physical gates.
- [ ] Commit: `test: verify private profile monitoring on real hardware`.

---

## Companion Plans

The Hardware Monitor client/agent plan depends on two companion implementations that are intentionally isolated from this repository:

1. **Doonchy Bridge/Gateway + phone PWA** — purpose-built private registry, telemetry/presence current-state store, authorized viewer API, and live phone dashboard.
2. **Native Android Agent** — implements the normalized platform adapter and publishes only Android telemetry actually exposed by the OS/device.

The immediate owner-use milestone is complete when a real Windows temperature from a bound user-created profile reaches the private Gateway and is rendered on the authenticated phone dashboard with correct ONLINE/STALE/OFFLINE semantics.
