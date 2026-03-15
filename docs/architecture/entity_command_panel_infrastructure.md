# Entity Command Panel Infrastructure

## Summary

The entity command panel infrastructure provides a reusable, multi-instance UI host for viewing entity command slots without binding to any specific input stack.

Implemented layers:

* `src/Core/UI/EntityCommandPanels/EntityCommandPanelContracts.cs`
  Core contracts for handles, anchors, sizes, slot views, source interfaces, and the service API.
* `src/Core/Commands/EntityCommandPanelCommands.cs`
  Trigger-friendly commands for open, close, show/hide, group switching, target rebinding, and layout updates.
* `mods/EntityCommandPanelMod/`
  Runtime implementation, GAS-backed source, handle store, source registry, and one retained `ReactivePage` host scene.
* `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/`
  Playable showcase over the interaction sandbox.

## Ownership

The runtime uses one host scene for all active panels.

Ownership decision:

* `EntityCommandPanelPresentationSystem` is the only owner that refreshes runtime revisions and syncs the mounted scene.
* `EntityCommandPanelController` owns one `ReactivePage<HostState>` and composes every visible panel card inside that single page.
* Individual panel instances do not mount their own `UiScene`.

This preserves the existing retained diff model in `Ludots.UI` and avoids creating a parallel UI runtime.

## Core API

### Service

`IEntityCommandPanelService` supports:

* `Open`
* `Close`
* `SetVisible`
* `RebindTarget`
* `SetGroupIndex`
* `CycleGroup`
* `SetAnchor`
* `SetSize`
* `TryGetState`

Handles use `Slot + Generation` semantics through `EntityCommandPanelHandle`.

### Trigger / Script API

Trigger-oriented control is exposed through:

* `OpenEntityCommandPanelCommand`
* `CloseEntityCommandPanelCommand`
* `SetEntityCommandPanelVisibilityCommand`
* `SetEntityCommandPanelGroupCommand`
* `RebindEntityCommandPanelTargetCommand`
* `SetEntityCommandPanelAnchorCommand`
* `SetEntityCommandPanelSizeCommand`

`IEntityCommandPanelHandleStore` provides alias-to-handle binding so triggers do not need to persist raw runtime handles.

### Source Model

`IEntityCommandPanelSource` is the data boundary:

* `TryGetRevision`
* `GetGroupCount`
* `TryGetGroup`
* `CopySlots`

The default reusable source id is `gas.ability-slots`, registered by `EntityCommandPanelMod`.

## GAS Group Semantics

The default GAS source exposes groups in this order:

1. `Current`
2. `Base`
3. one preview group per `AbilityFormSetDefinition` route
4. `Granted` when a `GrantedSlotBuffer` override exists

Slots are always rendered by slot index.

State flags currently distinguish:

* `Base`
* `FormOverride`
* `GrantedOverride`
* `TemplateBacked`
* `Empty`

## Runtime Shape

Hot-path instance state is stored in SoA arrays inside `EntityCommandPanelRuntime`:

* occupied flags
* visible flags
* targets
* source ids
* instance keys
* anchors
* sizes
* group indices
* generations
* observed revisions

Cold-path mappings remain in dictionaries:

* instance key -> slot
* alias -> handle
* form set id -> cached route labels

The host only recomposes when:

* panel lifetime changes
* visibility changes
* layout changes
* target/group changes
* source revision changes

## Showcase Flow

`EntityCommandPanelShowcaseMod` demonstrates:

1. auto-open pinned panels for `Arcweaver`, `Vanguard`, `Commander`, and `ArcweaverForms`
2. different anchors and card sizes per instance
3. form-group preview on `ArcweaverForms`
4. a hidden focus panel opened once and later controlled by trigger-style commands
5. dynamic `RebindTarget + SetVisible + SetAnchor + SetSize` when the selected entity changes

The interaction showcase's legacy panel is suppressed while this showcase is active through `InteractionShowcaseIds.SuppressUiPanelKey`.

## Current Constraints

Known limitation:

* `UIRoot` still mounts only one `UiScene` at a time.

Current mitigation:

* entity command panels share one retained host scene internally
* the interaction showcase legacy panel is explicitly suppressed during the demo

This means the infrastructure is reusable and multi-instance within its host, but global scene stacking across unrelated UI owners is still not solved by Core.

## Related Docs

* `docs/architecture/ui_runtime_architecture.md`
* `docs/architecture/gas_layered_architecture.md`
* `docs/architecture/trigger_guide.md`
* `docs/rfcs/RFC-0054-entity-command-panel-infra.md`
* `docs/rfcs/RFC-0055-ui-surface-ownership-and-showcase-takeover.md`
