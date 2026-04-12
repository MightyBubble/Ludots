# Navigation2D Flow Domains

## Why this exists

`Navigation2D` now treats large-world flowfields as an explicitly budgeted hotspot resource.

We do **not** solve one global 64km x 64km flowfield. Instead, we lease a small pool of bounded local flow domains to the groups that currently need crowd-flow guidance.

This keeps the production path aligned with Ludots rules:

- `Arch ECS` remains the gameplay/runtime SSOT.
- `SoA` remains solver cache only.
- no hidden global expansion when the world is large.
- no silent fallback when the hotspot budget is exhausted.

## Runtime chain

The formal chain is:

`Order/AI -> NavGroup runtime -> NavGroupFlowDomainAssignmentSystem -> Navigation2DFlowDomainPool -> CrowdFlow2D slot -> NavFlowBinding2D -> Navigation2DSteeringSystem2D`

Important boundaries:

- `NavGroup` is the unit that requests a hotspot flow domain.
- `CrowdFlow2D` remains the per-slot solver/cache.
- `Navigation2DFlowDomainPool` owns leasing, recentering, and oversubscription accounting.
- `NavFlowBinding2D` on members is derived runtime state, not authoring truth.

## Config surface

The pool is configured through `Navigation2D.FlowDomains`.

Key fields:

- `Enabled`: turns the pool on or off.
- `DomainCount`: exact number of concurrent hotspot flow slots.
- `DefaultProfileId`: explicit default profile used by group-level flow requests.
- `Profiles[]`: bounded local-domain definitions.

Each profile defines:

- `ActivationRadiusTiles`
- `MaxActiveTilesPerFlow`
- `UnloadGraceTicks`
- `MaxPotentialCells`
- `DomainWidthTiles`
- `DomainHeightTiles`
- `RecenterThresholdTiles`
- `HoldTicks`

`Navigation2D.FlowStreaming` still defines the global world bounds. The pool can only lease domains inside those explicit bounds.

## Allocation semantics

Current production semantics are intentionally simple and explicit:

- only non-`PreciseOrca` groups request a flow domain.
- each active group requests one domain centered on its current group target.
- request priority is derived from active group size.
- existing leases are reused when owner + profile still match.
- moving demand recenters the same slot once the recenter threshold is exceeded.
- when the pool is full, the system reports `UnassignedRequestCountFrame` instead of widening solve cost.
- higher-priority active hotspots can preempt stale or lower-priority leases.

There is no world-scale hidden solve window behind this system.

## Diagnostics

`NavDiagnosticsSnapshot` now exposes:

- `ActiveFlowDomains`
- `AssignedFlowDomains`
- `UnassignedFlowDomainRequests`
- `FlowDomainSummary`

This makes hotspot pressure visible in HUD/panel diagnostics instead of forcing developers to infer budget saturation from frame time alone.

## UAT

Acceptance artifacts for the large-world pool are written to:

`artifacts/acceptance/navigation2d-flow-domain-pool-large-world`

The current acceptance covers:

- three distant hotspots in a 64km x 64km world each receiving a bounded local domain.
- recentering an existing lease without global solve growth.
- explicit oversubscription reporting when a fourth hotspot exceeds a three-domain budget.

Primary automated checks live in:

- `src/Tests/Navigation2DTests/Navigation2DFlowDomainPoolTests.cs`
- `src/Tests/Navigation2DTests/Navigation2DFlowLargeWorldBudgetTests.cs`

## Production guidance

Use this path when:

- the world is large.
- crowd-flow demand is sparse and localized.
- group-level navigation needs explicit hotspot budgeting.

Do not use this path as an excuse to skip group ownership or runtime diagnostics. If a gameplay feature needs hotspot flow, it should produce a real `NavGroup`, let the runtime request a domain, and validate the outcome through diagnostics/UAT.
