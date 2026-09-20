# Champion Skill Sandbox Delivery Plan

Date: 2026-03-16
Status: Delivered

## Goal

Deliver a minimal playable champion skill sandbox mod that:

- lets the player select multiple Ezreal, Garen, and Jayce instances
- shows different command panel content based on the command-source primary and current form/state
- supports three global cast modes through one shared toolbar
- uses the existing GAS / entity collection / command panel / aim presentation pipelines
- gives every skill a clear visible effect expression through the presenter system
- supports camera reset, free camera, command-source primary follow, and weighted command-source group follow

## Confirmed Reuse Baseline

The current implementation must continue to reuse these existing foundations instead of building a parallel stack:

- Entity collection SSOT: `docs/architecture/entity_collection_query_infrastructure.md`
- Entity command panel infrastructure: `docs/architecture/entity_command_panel_infrastructure.md`
- GAS layered execution: `docs/architecture/gas_combat_infrastructure.md`
- Presenter pipeline: `docs/architecture/presentation_presenter.md`
- Camera interaction acceptance references:
  - `mods/fixtures/camera/CameraAcceptanceMod/`
  - `docs/audits/camera_acceptance_projection_marker_recovery.md`

Concrete code entry points already confirmed:

- Command panel GAS source: `mods/EntityCommandPanelMod/Runtime/GasEntityCommandPanelSource.cs`
- Ability form slot routing: `src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs`
- Ability form config loading: `src/Core/Gameplay/GAS/Config/AbilityFormSetConfigLoader.cs`
- Entity collection runtime: `src/Core/EntityCollections/` and `src/Core/Input/CommandSources/EntityCollectionContextRuntime.cs`
- Aim presentation projection: `mods/CoreInputMod/Systems/AbilityAimPresentationProjectionSystem.cs` + `src/Core/Input/Orders/AbilityAimPresentationRuntime.cs`
- Presenter runtime:
  - `src/Core/Presentation/Systems/PresenterRuleSystem.cs`
  - `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs`
  - `src/Core/Presentation/Systems/PresenterEmitSystem.cs`
- Projectile runtime entity creation:
  - `src/Core/Gameplay/GAS/BuiltinHandlers.cs`
  - `src/Core/Gameplay/GAS/Systems/ProjectileRuntimeSystem.cs`

## Final Delivery State

Implemented in the sandbox branch:

- multi-instance Ezreal / Garen / Jayce sandbox mod with command panel focus sync
- ability form slot routing for Jayce via GAS ability-form infra
- three cast modes:
  - `SmartCast`
  - `SmartCastWithIndicator`
  - `PressReleaseAimCast`
- command panel toolbar mode switching
- command marker and hover marker
- camera confine and edge-pan safety for the sandbox tactical camera
- camera reset button
- camera toolbar follow modes:
  - free
  - command-source primary
  - weighted command-source group
- projectile skills now use real GAS projectile entities with presenter bootstrap
- sandbox cast / hit / projectile cues now go through presenter definitions and `PresentationCommandBuffer`
- command panel keeps reusing existing ability-slot routing and icon generation

Recent camera-follow slice commit:

- `c7cb67c feat(camera): add selected-group follow mode`

Recent projectile / presentation slices:

- `283c472 feat(presentation): bridge projectile entities into presenters`
- `abe91e8 feat(gas): preserve projectile target points`

Recent sandbox interaction / feedback slices:

- `92776b3 feat(champion-sandbox): add movement and hover target marker`
- `964c11e feat(champion-sandbox): add command marker and cast feedback`
- `8a444f7 feat(champion-sandbox): add camera reset and tactical confine`

## Confirmed Architecture Outcome

### 1. Projectile is a real ECS entity

`LaunchProjectile` creates a runtime projectile entity through `BuiltinHandlers.HandleCreateProjectile`.
That entity is moved by `ProjectileRuntimeSystem`, keeps launch-origin / target-point data, and can now receive startup presenters through the shared projectile-presentation binding registry.

Relevant code:

- `src/Core/Gameplay/GAS/BuiltinHandlers.cs`
- `src/Core/Gameplay/GAS/Systems/ProjectileRuntimeSystem.cs`
- `src/Core/Presentation/Projectiles/ProjectilePresentationBindingRegistry.cs`
- `src/Core/Presentation/Systems/ProjectilePresentationBootstrapSystem.cs`

Result:

- projectile logic and projectile visuals stay on the same entity-backed runtime path
- sandbox projectile visuals are declared in config instead of special-case presenter code

### 2. Skill FX are presenter-driven

Sandbox cast / hit feedback now uses `GasPresentationEventBuffer` + `PresentationCommandBuffer` and resolves presenter ids from:

- ability ids for cast cues
- effect template ids for hit cues
- projectile impact effect ids for projectile-body startup presenters

Relevant code:

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxVisualFeedback.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/presenters.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/projectile_cues.json`

Result:

- every showcased skill now has a distinct primitive-based cast and/or hit expression
- Ezreal Q / R and Jayce Cannon Q additionally have visible traveling projectile bodies
- damage readability remains backed by `WorldHudBatchBuffer`

### A. Keep slot/form logic in GAS, not in the sandbox

The user reminder was correct: ability-form route-to-slot already exists and should remain the single source of truth.

The sandbox will keep using:

- `AbilityFormSetRegistry`
- `AbilityFormRoutingSystem`
- command panel effective-slot resolution

### B. Reuse the shared projectile-to-presenter bridge instead of fake sandbox-only missiles

Projectile remains a real ECS entity.

The reusable extension now attaches presenter startup state to runtime projectile entities so that projectile visuals can be authored declaratively and reused by mods.

Target outcome:

- projectile entity is still moved by `ProjectileRuntimeSystem`
- projectile entity gains presenter bootstrap state for visible primitive VFX
- sandbox authors only need config bindings, not custom projectile presenter code

### C. Skill cast / hit cues go through presenter commands

For non-projectile skill feedback, the sandbox emits `PresentationCommand.CreatePresenter` based on `GasPresentationEvent` so cues can anchor to:

- caster for cast windup / self-buff / aura cues
- target for impact / execute / hit cues

This keeps feedback inside the presenter system while allowing per-skill anchor choice.

### D. Primitive-first visuals are sufficient

For this sandbox, visible primitive effects are acceptable and preferred over introducing incomplete art pipelines.

Expected visual vocabulary:

- projectile body: sphere / pulse marker
- zone skill: ring / circle / rectangle overlay
- melee / strike skill: short burst marker on actor or target
- toggle / aura skill: persistent ring or marker presenter

## Implementation Slices

### Slice 1. Camera follow toolbar

Completed.

Evidence:

- `src/Core/Gameplay/Camera/FollowTargets/EntityCollectionGroupFollowTarget.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillCastModeToolbarProvider.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxConfigTests.cs`
- `src/Tests/ThreeCTests/CameraRuntimeConvergenceTests.cs`

### Slice 2. Reusable projectile presenter bootstrap

Completed Core work:

- add a generic projectile presentation binding/bootstrap path
- ensure runtime-created projectile entities can receive startup presenters and stable presentation identity
- cover the bootstrap path with focused GAS / presentation tests

### Slice 3. Convert sandbox projectile skills to real `LaunchProjectile`

Completed sandbox work:

- convert Ezreal Q / R and Jayce Cannon Q from `Search` to `LaunchProjectile`
- author projectile presenter bindings/config
- keep impact damage in existing GAS hit effects

### Slice 4. Replace generic sandbox feedback with per-skill presenter FX

Completed sandbox work:

- add presenter definitions for cast, projectile, hit, aura, and execute cues
- route sandbox visual feedback through `PresentationCommandBuffer`
- make every showcased skill visibly different

### Slice 5. Acceptance verification

Required evidence:

- targeted unit/config tests
- runnable raylib sandbox launch
- visual verification of:
  - command marker
  - hover marker
  - cast mode switching
  - projectile spawn/travel/hit visibility
  - camera follow modes

## Acceptance Checklist

- changing the command-source primary champion changes the command panel immediately
- Jayce form switching changes current slots through GAS form routing
- three cast modes behave differently and can be switched from the toolbar
- move order remains usable for spacing
- selection and hover are visually legible
- projectile skills spawn visible presenter-driven missiles
- every skill has a distinct cast and/or impact effect cue
- camera can reset, stay confined, and follow the command-source primary or weighted group

## Notes

- Command panel icons are still produced by the existing `EntityCommandPanelMod` icon pipeline. The slot data exposes `AbilityId + ActionId`, and the UI derives icon glyph / mode badge from ability presentation plus current interaction mode.
- This means one ability can participate in different orders or interaction modes without introducing a parallel icon system in the sandbox.

## Open Risk

The current projectile runtime only stores logic travel data. If later features need richer projectile-specific rendering metadata beyond startup presenters, a dedicated shared projectile presentation contract may still be needed in Core.
