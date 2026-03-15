# Entity Selection Architecture

## Scope

This document defines the formal entity-selection architecture used by Ludots input, camera/view bridges, and order submission.

The goals are:

- keep formal selection separate from `TabTarget`
- keep selection storage independent from player-only ownership
- allow one selector entity to own multiple named selection sets
- keep order submission decoupled from selection storage
- keep temporary gameplay unselectability separate from stable system capability

## Single Source Of Truth

Formal selection state is selector-owned ECS data.

- ambient/default selection lives on the selector entity via `SelectionBuffer`
- additional named sets live on dedicated entities keyed by `SelectionSetOwner` + `SelectionSetId`
- `SelectionRuntime` is the only runtime API for reading/writing selection sets

Relevant code:

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionMaintenanceSystem.cs`

This means selection is not hard-bound to "player". Any live entity can act as a selector owner, including local players, AI controllers, bosses, NPCs, or debug-only selector entities.

## Formal Acquisition Rules

Screen-space formal selection uses `VisualTransform` as the pick standard.

- click and box selection: `EntityClickSelectSystem`
- tab target cycling: `TabTargetCycleSystem`
- both require `SelectionEligibility.IsSelectableNow(...)`

Formal acquisition eligibility is split into two layers:

- `SelectionSelectableTag`: stable system/config capability label
- `SelectionSelectableState`: runtime gameplay gate for temporary states such as untargetable, stasis, or "golden body"

Relevant code:

- `src/Core/Input/Selection/EntityClickSelectSystem.cs`
- `mods/CoreInputMod/Systems/TabTargetCycleSystem.cs`
- `src/Core/Input/Selection/SelectionEligibility.cs`
- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Config/ComponentRegistry.cs`

`SelectionSelectableState` gates new acquisition only. Existing selection sets are not retroactively pruned when a target becomes temporarily unavailable; only dead entities are pruned automatically. This preserves selector-owned selection data for AI/debug workflows and avoids hidden policy coupling.

## View Bridge And Compatibility

Viewed selection is explicit and separate from storage.

- `SelectionViewOwnerEntity` selects which selector owner is currently being projected
- `SelectionViewSetKey` selects which set on that owner is being projected
- `SelectionViewRuntime` resolves the viewed set
- `SelectionBridgeProjectionSystem` projects that viewed set into compatibility outputs

Compatibility outputs are derived only:

- `SelectedTag` on viewed entities
- `CoreServiceKeys.SelectedEntity` for legacy readers

Relevant code:

- `src/Core/Input/Selection/SelectionViewRuntime.cs`
- `src/Core/Input/Selection/SelectionBridgeProjectionSystem.cs`

`SelectedEntity` is not the selection source of truth.

## Selection Versus Order

Selection and order are intentionally separate concepts.

- selection answers "which entities are currently grouped under selector/set X"
- order answers "which actor submits which order payload"

`InputOrderMapping.SelectionSetKey` only chooses which selection set is used when a mapping needs entity arguments. The order actor still comes from `ActorProvider`, not from selection storage itself.

Relevant code:

- `src/Core/Input/Orders/InputOrderMapping.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs`
- `mods/MobaDemoMod/Systems/MobaLocalOrderSourceSystem.cs`

This keeps `order` and `selection` loosely coupled:

- selection can feed entity parameters into an order
- selection does not define the order actor
- the same selector can maintain multiple named sets for different commands

## Ability Selection Gates

Ability-driven selection gates reuse the same formal selectability contract for entity acquisition.

- `GasSelectionResponseSystem` filters entity results through `SelectionEligibility`
- relationship filtering remains separate and additive

Relevant code:

- `src/Core/Input/Selection/GasSelectionResponseSystem.cs`
- `src/Tests/GasTests/InteractionSelectionConvergenceTests.cs`
