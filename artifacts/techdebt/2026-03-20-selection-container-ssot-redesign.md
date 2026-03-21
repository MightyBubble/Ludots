# Tech Debt Report: TD-2026-03-20-selection-container-ssot-redesign

Date: 2026-03-20
Reporter: Codex
Owner: Core Interaction / Core Input / Camera / Showcase Mods / Test Owners
Severity: P1
Scope: Cross-layer

## Trigger

The selection SSOT redesign has been implemented in core and accepted by the champion sandbox/stress acceptance chain, but repository-wide audit still shows stale legacy references outside the delivered path.

Primary delivery evidence:

- `docs/architecture/entity_selection_architecture.md`
- `docs/rfcs/RFC-0059-entity-selection-container-ssot.md`
- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Gameplay/GAS/Orders/OrderArgs.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSelectionLeaseCleanupSystem.cs`
- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-stress/battle-report.md`

## Current Accepted State

Delivered and verified:

- selection truth is container entity + member relation entity
- viewed selection resolves through explicit viewer + view key
- orders reference selection containers and can lease snapshot containers
- champion stress can switch between player live, player formation, AI targets, AI formation, and command snapshot without a compatibility bridge
- the old `64` selection cap has been removed from the delivered selection/order path

## Residual Debt

### 1. Legacy names still exist in core key registries

Residual legacy keys still present:

- `src/Core/Scripting/CoreServiceKeys.cs`
  - `SelectedEntity`
  - `SelectionViewOwnerEntity`
  - `SelectionViewSetKey`
- `src/Core/Scripting/ContextKeys.cs`
  - `SelectedEntity`
  - `SelectionViewOwnerEntity`
  - `SelectionViewSetKey`

Risk:

- these names can invite new code to rebuild compatibility patterns even though runtime SSOT has moved on

Required follow-up:

- either remove them after dependent tests/mods migrate, or mark them as forbidden legacy debt with explicit migration deadline

### 2. Out-of-scope tests still encode pre-SSOT assumptions

Known stale tests:

- `src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
- `src/Tests/ThreeCTests/CameraRuntimeConvergenceTests.cs`
- `src/Tests/ThreeCTests/CameraShowcaseModTests.cs`
- `src/Tests/Navigation2DTests/Navigation2DPlaygroundPlayableAcceptanceTests.cs`

Observed stale assumptions include:

- direct writes to `CoreServiceKeys.SelectedEntity`
- assertions against `SelectedTag`
- direct use of `SelectionBuffer`
- capacity assumptions tied to old fixed-size selection storage

Risk:

- future work can be blocked or misled by tests that still describe removed architecture

Required follow-up:

- migrate these tests to `SelectionRuntime`, `SelectionContextRuntime`, and explicit viewed-selection contracts

### 3. Some docs outside the backwritten SSOT still describe removed concepts

Known stale docs found during audit:

- `docs/rfcs/RFC-0053-entity-info-panels-for-ui-and-overlay.md`
- historical references outside the updated selection architecture packet

Risk:

- readers can still discover removed compatibility concepts from older documents and reuse them incorrectly

Required follow-up:

- audit all selection-facing docs for `SelectedEntity`, `SelectedTag`, `SelectionBuffer`, and `SelectionGroupBuffer`

## Fuse Decision

Mode: hard stop on new legacy usage

Rules from this report onward:

- no new code may depend on `SelectedEntity`
- no new code may depend on `SelectedTag`
- no new selection truth may be stored in fixed-capacity buffers
- no new feature may introduce a bridge or mirror as a second truth

## Containment

Current containment already in place:

- architecture SSOT backwritten to `docs/architecture/entity_selection_architecture.md`
- RFC updated to reflect delivered design and evidence
- champion sandbox/stress acceptance verifies the live path
- view-mode switching in champion sandbox now uses an explicit contract helper instead of relying on fragile cross-context strong typing

## Due Window

- next migration window: before enabling any new camera/panel/selection feature work on top of ThreeCTests or Navigation2D tests
- hard requirement: clear stale legacy tests before deleting remaining legacy key names from core
