# Order Spatial Resolution and Hover Feedback

> SSOT scope: shared order world-space resolution, and how a showcase draws selection and hover without a private input mod.
> Move-path overlay events are not an engine system. This page only describes what the repository actually runs.

## 1. Runtime Intent

Two jobs stay on the existing pipelines:

- order code resolves a world point from `OrderSpatial` in one place
- selection and hover rings are presenter rules on collection membership

Orders enter through trigger graphs. `SubmitCommandIntent` / `SubmitCast` land in `CommandIntentBufferDrainSystem`, which calls `CompositeOrderPlanner` when a cast must move first. There is no local order-source installer.

## 2. Shared Spatial Resolution

`src/Core/Gameplay/GAS/Orders/OrderWorldSpatialResolver.cs` reads `OrderSpatial`:

- `GetSpatialPointCount`
- `TryResolveSpatialPointAt`

`OrderSubmitter`, `CompositeOrderPlanner`, and `MovePlanStore` read points through it. Queued-order projection uses `TryResolveProjectedQueuedOrigin`.

## 3. Selection and Hover

The champion sandbox does not poll a hover helper. `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/presenters.json` listens:

- `EntityCollectionMemberAdded` / `Removed` on `role:commandSource` creates and destroys the selection ring
- the same events on `collection.ui.command.hover` create and destroy the hover ring

`role:commandSource` is resolved from `Input/collection_keys.json` at load. A literal collection key has no `role:` prefix. The engine root does not declare `commandSource`; the session that opens the map does.

Pointer samples go through `GraphPointerSampleRuntime` (`Ludots.Core.Input.PointerSample`). A pointer region whose profile sets `pointerRouting` to `ui` is pushed by `PointerRegionContextSystem`. The minimap profile is `interaction.context.minimap`.

Camera jumps submit `CameraPoseRequest`. They do not write the rig from an input consumer.

## 4. Acceptance Evidence

- `src/Core/Gameplay/GAS/Orders/OrderWorldSpatialResolver.cs`
- `src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs`
- `src/Core/Input/Orders/CommandIntentBufferDrainSystem.cs`
- `src/Core/Input/PointerSample/GraphPointerSampleRuntime.cs`
- `src/Tests/GasTests/Ability/OrderCompositePlannerTests.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/presenters.json`
