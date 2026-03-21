# Entity Selection Architecture

## Scope

This document is the architecture SSOT for Ludots selection storage, viewed selection, order-bound selection snapshots, and selection-facing consumers such as camera, panels, overlays, and debug tooling.

The goals are:

- keep selection truth in ECS entities and relations
- allow any entity to own or view selection state
- keep input as mutation intent, not selection truth
- keep orders, formations, panels, and cameras consuming the same viewed-selection contract
- forbid semantic caps such as hardcoded `64` on selection truth

## Single Source Of Truth

Formal selection state lives in container entities plus member-relation entities.

- container entity:
  - `SelectionContainerTag`
  - `SelectionContainerOwner`
  - `SelectionContainerAliasId`
  - `SelectionContainerKindComponent`
  - `SelectionContainerRevision`
  - `SelectionContainerMemberCount`
- member relation entity:
  - `SelectionMemberTag`
  - `SelectionMemberContainer`
  - `SelectionMemberTarget`
  - `SelectionMemberOrdinal`
  - `SelectionMemberRoleId`

`SelectionRuntime` is the only runtime mutation/query API for formal selection storage.

Relevant code:

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionMaintenanceSystem.cs`

This means selection is not player-only. A local player, AI commander, boss controller, replay inspector, debug viewer, or order lease owner can all participate as ordinary entities.

## Container Model

Selection containers are keyed by `(owner entity, alias key)` and classified by `SelectionContainerKind`.

Current built-in kinds:

- `Live`
- `Snapshot`
- `Group`
- `Formation`
- `Derived`
- `CommandBinding`
- `Debug`

Current built-in aliases and views:

- selection aliases:
  - `selection.live.primary`
  - `selection.formation.primary`
  - `selection.command.preview`
  - `selection.command.snapshot`
- view keys:
  - `selection.view.primary`
  - `selection.view.secondary`
  - `selection.view.command-preview`
  - `selection.view.formation`
  - `selection.view.debug`

Relevant code:

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`

## Mutation And Query Contract

Selection writers must go through `SelectionRuntime`.

Important operations:

- create or resolve containers:
  - `TryGetSelectionEntity(...)`
  - `TryGetOrCreateSelectionEntity(...)`
  - `TryGetOrCreateContainer(...)`
- mutate membership:
  - `ReplaceSelection(...)`
  - `AddToSelection(...)`
  - `RemoveFromSelection(...)`
  - `ClearSelection(...)`
- clone or snapshot:
  - `TryCloneSelection(...)`
  - `TryCreateSnapshotLease(...)`
- bind views:
  - `TryBindView(...)`
  - `TryResolveViewContainer(...)`
- describe containers and views for consumers:
  - `TryDescribeContainer(...)`
  - `TryDescribeSelection(...)`
  - `TryDescribeView(...)`

Read-side helpers for consumers:

- `SelectionContextRuntime.TryGetCurrentPrimary(...)`
- `SelectionContextRuntime.TryGetCurrentContainer(...)`
- `SelectionContextRuntime.CopyCurrentSelection(...)`
- `SelectionContextRuntime.TryDescribeCurrentView(...)`

Relevant code:

- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionContextRuntime.cs`
- `src/Core/Input/Selection/SelectionViewDescriptors.cs`

## Acquisition Rules

Input produces selection mutations against formal containers.

Current acquisition systems:

- click and box selection:
  - `src/Core/Input/Selection/EntityClickSelectSystem.cs`
- ability-driven selection responses:
  - `src/Core/Input/Selection/GasSelectionResponseSystem.cs`
- tab target cycling:
  - `mods/CoreInputMod/Systems/TabTargetCycleSystem.cs`

Eligibility remains split into stable capability and temporary runtime gate:

- `SelectionSelectableTag`
- `SelectionSelectableState`
- `SelectionEligibility`

Existing selection is not automatically pruned when a unit becomes temporarily unselectable. Automatic maintenance only removes dead members. This keeps AI/debug/order snapshots stable and avoids hidden policy rewrites.

## Viewed Selection

Viewed selection is explicit and separate from storage.

- storage truth lives in containers and member relations
- a viewer entity binds a `view key` to a container
- consumers resolve the active viewed selection from:
  - `CoreServiceKeys.SelectionViewViewerEntity`
  - `CoreServiceKeys.SelectionViewKey`

`SelectionViewRuntime` resolves the active viewed selection, and `SelectionContextRuntime` exposes consumer-facing helpers on top of it.

Relevant code:

- `src/Core/Input/Selection/SelectionViewRuntime.cs`
- `src/Core/Input/Selection/SelectionContextRuntime.cs`

Consumers must not recreate a second truth such as `SelectedEntity`, `SelectedTag`, or player-only ambient buffers.

## Orders And Selection Snapshots

Orders no longer embed a fixed-capacity entity array for selected targets.

Formal order-side selection now uses:

- `OrderSelectionReference`
- `OrderArgs.Selection`

When an order must keep a stable selection snapshot after submission, the snapshot is materialized as a selection container and retained by a lease owner entity.

Relevant code:

- `src/Core/Gameplay/GAS/Orders/OrderArgs.cs`
- `src/Core/Gameplay/GAS/Orders/OrderQueue.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSelectionLeaseCleanupSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

This is the selection-order contract:

- actor resolution still comes from the order actor provider
- selection contributes entity collections or stable snapshots
- queued orders retain container references, not a duplicated fixed-size payload

## Panels, Camera, And Mod Consumers

Panels, camera follow targets, overlays, and showcase mods must consume viewed-selection APIs or descriptor APIs, not selection storage internals.

Selection-facing consumers should resolve through:

- `SelectionContextRuntime`
- `SelectionRuntime.TryDescribeView(...)`
- `SelectionRuntime.TryDescribeContainer(...)`

The champion sandbox stress harness is the reference acceptance mod for this contract:

- player live selection view
- player formation view
- AI target view
- AI formation view
- command snapshot view

Relevant code:

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillCastModeToolbarProvider.cs`
- `src/Core/Commands/EntityCommandPanelCommands.cs`

## Formation And Control-Group Semantics

Formation and control-group semantics reuse the same selection container truth.

- formation is a container kind and/or alias choice, not a second member-truth structure
- command previews and command snapshots are containers or leased snapshot containers
- multiple viewers can inspect different containers at the same time

This allows:

- player-selected units
- AI-selected targets
- boss-selected victims
- debug-inspected formation groups
- order-bound snapshots

to coexist without compatibility projections.

## Budgets And Prohibitions

Selection truth must not encode a semantic member cap.

Allowed:

- runtime budgets such as `SelectionRuntimeConfig.MutationApplyBudgetPerFrame`
- UI windowing, truncation, or virtualization at presentation boundaries
- telemetry-driven cost controls

Forbidden:

- hardcoded semantic caps like `SelectionBuffer.CAPACITY = 64`
- silent truncation of formal selection truth
- consumers treating `SelectedEntity` or `SelectedTag` as authoritative
- mod-local parallel selection storage for the same gameplay truth

`OrderSpatial.MaxPoints = 64` is a spatial payload budget for multi-point geometry, not selection truth. It must not be reused as a selection limit.

## Acceptance Evidence

Reference acceptance evidence for the delivered architecture:

- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-sandbox/trace.jsonl`
- `artifacts/acceptance/champion-skill-sandbox/path.mmd`
- `artifacts/acceptance/champion-skill-stress/battle-report.md`
- `artifacts/acceptance/champion-skill-stress/trace.jsonl`
- `artifacts/acceptance/champion-skill-stress/path.mmd`
- `artifacts/acceptance/champion-skill-stress/screens/timeline.svg`

## Residual Debt

Any remaining references to `SelectionBuffer`, `SelectionGroupBuffer`, `SelectedEntity`, or `SelectedTag` outside the contracts above are migration debt, not architecture.

The active debt inventory is tracked in:

- `artifacts/techdebt/2026-03-20-selection-container-ssot-redesign.md`
