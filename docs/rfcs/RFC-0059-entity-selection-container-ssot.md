# RFC-0059 Entity-Relation Selection Container SSOT

Status: Implemented and backwritten to architecture on 2026-03-20  
Architecture SSOT: `docs/architecture/entity_selection_architecture.md`

## 1. Decision

Selection truth is modeled as:

- selection container entities
- selection member relation entities
- explicit viewer-to-view bindings
- order-bound container references and leased snapshot containers

This RFC rejects:

- fixed-capacity selection truth such as `SelectionBuffer` or `SelectionGroupBuffer`
- player-only selection ownership
- compatibility projections becoming de facto truth
- order payloads embedding a second fixed-capacity selection copy

## 2. Why This RFC Existed

The old direction had three architectural failures:

1. multiple truths:
   - formal selection storage
   - `SelectedEntity`
   - `SelectedTag`
   - player-only ambient assumptions
2. semantic limits hidden inside storage:
   - hardcoded `64` treated as a gameplay truth limit
3. fractured consumers:
   - panel, camera, order, debug, formation, and sandbox each read selection differently

That model could not support:

- player and AI viewing different selections at the same time
- boss/debug inspection of other actors' selections
- command history keeping stable selection snapshots
- formation and live selection reusing the same member truth

## 3. Accepted Model

### 3.1 Containers

Each formal selection is a container entity keyed by `(owner entity, alias key)`.

Implemented components:

- `SelectionContainerTag`
- `SelectionContainerOwner`
- `SelectionContainerAliasId`
- `SelectionContainerKindComponent`
- `SelectionContainerRevision`
- `SelectionContainerMemberCount`

Implemented kinds:

- `Live`
- `Snapshot`
- `Group`
- `Formation`
- `Derived`
- `CommandBinding`
- `Debug`

### 3.2 Members

Each membership edge is a relation entity.

Implemented components:

- `SelectionMemberTag`
- `SelectionMemberContainer`
- `SelectionMemberTarget`
- `SelectionMemberOrdinal`
- `SelectionMemberRoleId`

### 3.3 Views

Viewed selection is not implicit. A viewer entity binds a view key to a container.

Implemented components and helpers:

- `SelectionViewBindingTag`
- `SelectionViewBindingViewer`
- `SelectionViewBindingKeyId`
- `SelectionViewBindingContainer`
- `SelectionViewRuntime`
- `SelectionContextRuntime`
- `SelectionViewDescriptor`
- `SelectionContainerDescriptor`

### 3.4 Orders

Orders reference selection containers instead of embedding a fixed-capacity selected-entity payload.

Implemented order-side contract:

- `OrderSelectionReference`
- `OrderArgs.Selection`
- `SelectionRuntime.TryCreateSnapshotLease(...)`
- `OrderSelectionLeaseCleanupSystem`

## 4. Hexagonal Boundaries

### Input adapter boundary

Input only produces mutation intent and view-choice intent.

Input must not:

- write `SelectedEntity`
- invent a second storage structure
- assume the current viewer must be the local player

### Application boundary

`SelectionRuntime` owns:

- container creation and resolution
- membership mutation
- cloning and snapshot leasing
- view binding
- container and view descriptors

### Output adapter boundary

Panel, camera, overlay, sandbox, and debug consumers must read through:

- `SelectionContextRuntime`
- `SelectionRuntime.TryDescribeView(...)`
- `SelectionRuntime.TryDescribeContainer(...)`

They must not reintroduce compatibility truth.

## 5. Formation And Multi-Viewer Support

Formation is not a second truth structure.

Formation, live selection, AI target selection, and command snapshots all reuse the same container/member model with different:

- owners
- aliases
- kinds
- viewers

This is what allows the champion stress harness to switch between:

- player live
- player formation
- AI targets
- AI formation
- command snapshot

without any compatibility bridge.

## 6. Implementation Evidence

Core implementation:

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionContextRuntime.cs`
- `src/Core/Input/Selection/SelectionViewDescriptors.cs`
- `src/Core/Gameplay/GAS/Orders/OrderArgs.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSelectionLeaseCleanupSystem.cs`

Champion sandbox and stress acceptance:

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillCastModeToolbarProvider.cs`
- `src/Tests/GasTests/InteractionSelectionConvergenceTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxConfigTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxPlayableAcceptanceTests.cs`

Acceptance artifacts:

- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-sandbox/trace.jsonl`
- `artifacts/acceptance/champion-skill-sandbox/path.mmd`
- `artifacts/acceptance/champion-skill-stress/battle-report.md`
- `artifacts/acceptance/champion-skill-stress/trace.jsonl`
- `artifacts/acceptance/champion-skill-stress/path.mmd`
- `artifacts/acceptance/champion-skill-stress/screens/timeline.svg`

## 7. Explicit Non-Decisions

This RFC does not reintroduce:

- a compatibility `SelectedEntity2`
- a legacy facade over removed fixed-capacity selection truth
- a mod-local parallel selection engine
- a second formation-only member store

## 8. Residual Debt

Residual debt is migration work outside the accepted architecture, not open design uncertainty.

Tracked debt:

- `artifacts/techdebt/2026-03-20-selection-container-ssot-redesign.md`
