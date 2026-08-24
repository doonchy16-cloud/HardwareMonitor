# Phase 7 — Central Profile Registry + Cache Sync Implementation Plan

## Goal
Make profile configuration centrally authoritative through the existing Doonchy Bridge Gateway while preserving safe local monitoring and editing during temporary Gateway outages.

## Locked boundaries
- Reuse the existing profile contracts, editor, presence, telemetry routing, Agent, and Bridge identity/auth transport.
- Never persist Bridge credentials/tokens in Hardware Monitor registry or sync-state files.
- Every authoritative registry mutation uses optimistic concurrency and increments a monotonic revision.
- Every client keeps a schema-validated, atomically written local cache with last-known-good recovery.
- Offline edits are explicit pending changes; a stale expected revision becomes Conflict and must never overwrite newer authority.
- Phase 8 phone pairing/viewer work is out of scope.

## Tasks
1. Add registry revision semantics and preserve revisions through local edits.
2. Add last-known-good cache backup/recovery tests and implementation.
3. Add persistent local sync metadata and a transport-independent sync coordinator.
4. Add a Bridge Gateway registry client for authenticated pull/push with explicit conflict handling.
5. Wire the background Agent to perform startup/periodic sync without blocking sensor collection or telemetry.
6. Route Profiles page mutations through the sync-aware local cache so offline edits become pending rather than pretending to be authoritative.
7. In the Bridge repo, add a dedicated versioned Hardware Monitor registry contract, central store, authenticated GET/POST routes, optimistic concurrency, bounds, and tests.
8. Add Phase-7 smoke evidence for pull, push, revision increment, outage cache survival, and conflict preservation.
9. Run full unit/hosted/self-hosted real-hardware gates, merge exact reviewed heads, deploy Gateway main only to the permanent Gateway host, and require post-merge real-hardware success.

## Acceptance
- Registry revision 0 bootstrap and monotonic commits are proven.
- Corrupt primary local cache recovers from validated last-known-good state.
- Gateway unavailable: cached profiles still drive monitoring; sync state is stale/pending.
- Matching expected revision: pending edit commits and cache advances to returned revision.
- Stale expected revision: Conflict is surfaced and local edit remains intact.
- No secret/token appears in registry/cache/diagnostics or test output.
- Existing Phase-6 telemetry and real-hardware gates remain green.
