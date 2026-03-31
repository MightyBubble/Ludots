# Control Buff Infrastructure

Status: implemented

## Overview

This document defines the reusable control-buff baseline that now ships in Ludots for common crowd-control gameplay:

- `slow`
- `silence`
- `root`
- `stun`

The implementation follows the Ludots reuse-first rule:

- slow stays on the existing `MoveSpeed` attribute chain
- action and movement lockouts project through a shared sink into runtime control state
- showcase content lives in mods, while reusable runtime contracts live in Core

The authoritative gameplay path is:

`Effect -> AttributeAggregator -> AttributeBinding(Gameplay.ControlState sink) -> GameplayControlState -> runtime consumers`

This avoids per-showcase movement or cast-block special cases.

## Scope

In scope for v1:

- reusable Core control-state runtime contract
- reusable `CommonControlBuffsMod`
- playable champion showcase map and entry mod
- headless acceptance evidence under `artifacts/acceptance/champion-control-showcase/`

Out of scope for v1:

- `disarm`
- fear, taunt, sleep, knock-up, airborne routing
- a separate control-only runtime stack

## Core Design

### 1. Slow stays on `MoveSpeed`

`slow` is not part of `GameplayControlState`.

It remains an ordinary attribute modifier on `MoveSpeed`, so every existing consumer of resolved movement speed continues to work without a parallel slow pipeline.

Relevant paths:

- `mods/capabilities/gameplay/CommonControlBuffsMod/assets/GAS/effects.json`
- `src/Core/Gameplay/GAS/Systems/MoveToWorldCmOrderSystem.cs`
- `src/Core/Navigation2D/Systems/NavOrderAgentBootstrapSystem.cs`

### 2. Control state projects through a sink

Core introduces `GameplayControlState` plus a built-in sink named `Gameplay.ControlState`.

Authoritative channels:

- `Control.MoveBlockCount`
- `Control.ActionBlockCount`

Current projection rules:

- `MoveBlocked = Control.MoveBlockCount > 0`
- `ActionBlocked = Control.ActionBlockCount > 0`

Relevant paths:

- `src/Core/Gameplay/GAS/Components/GameplayControlState.cs`
- `src/Core/Gameplay/GAS/Bindings/GameplayControlStateSink.cs`
- `src/Core/Gameplay/GAS/Bindings/GasAttributeSinks.cs`
- `mods/capabilities/gameplay/CommonControlBuffsMod/assets/GAS/attribute_bindings.json`

### 3. Runtime consumers read `GameplayControlState`

Movement consumers:

- `src/Core/Gameplay/GAS/Systems/MoveToWorldCmOrderSystem.cs`
- `src/Core/Navigation2D/Systems/NavOrderAgentBootstrapSystem.cs`

Behavior:

- move-blocked actors resolve to zero movement speed
- active navigation goals are cleared when speed resolves to zero
- residual `Velocity2D`, `NavDesiredVelocity2D`, and `ForceInput2D` are zeroed so control takes effect immediately

Cast consumers:

- `src/Core/Gameplay/GAS/GameplayControlStateResolver.cs`
- `src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`

Behavior:

- startup is rejected when `ActionBlocked` is active
- in-flight exec is interrupted when `ActionBlocked` becomes active

This makes `silence` and `stun` reusable without repeating bespoke cast-block logic in every showcase ability.

## Mod Packaging

### `CommonControlBuffsMod`

Reusable gameplay content lives in:

- `mods/capabilities/gameplay/CommonControlBuffsMod/`

Optional presentation content lives in:

- `mods/capabilities/gameplay/CommonControlBuffsPresentationMod/`

`CommonControlBuffsMod` provides:

- common effect templates in `assets/GAS/effects.json`
- control attribute bindings in `assets/GAS/attribute_bindings.json`
- attribute constraints in `assets/GAS/attribute_constraints.json`
- semantic tag rules in `assets/GAS/tag_rules.json`

`CommonControlBuffsPresentationMod` provides:

- persistent status performers in `assets/Presentation/performers.json`
- runtime status presentation in `Runtime/CommonControlStatusPresentationSystem.cs`

This split keeps the gameplay capability reusable in non-presentation or server-style contexts while still letting showcases opt into visible status performers.

Implemented reusable effects:

- `Effect.Control.Common.Slow.Light`
- `Effect.Control.Common.Slow.Heavy`
- `Effect.Control.Common.Silence`
- `Effect.Control.Common.Root`
- `Effect.Control.Common.Stun`

Effect semantics:

- slow: modifies `MoveSpeed`
- silence: adds `Control.ActionBlockCount` and grants `Status.Silenced`
- root: adds `Control.MoveBlockCount` and grants `Status.Rooted`
- stun: adds both `Control.MoveBlockCount` and `Control.ActionBlockCount`, then grants `Status.Stunned`

### Status tags

Status tags remain useful for:

- presentation
- debug output
- downstream content semantics

The authoritative gameplay lockout, however, is `GameplayControlState`, not attached tag visibility.

That distinction is important for future mods: new control effects should project their blocking behavior through the sink, and use tags as semantic surface area rather than as the only authority for movement or cast denial.

## Showcase Design

### Host mod

The playable showcase extends the existing champion sandbox rather than creating a parallel runtime.

Relevant paths:

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Maps/champion_control_showcase.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Systems/ChampionControlShowcaseSystem.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillControlShowcaseOverlay.cs`
- `mods/showcases/champion_control_showcase_entry/ChampionControlShowcaseEntryMod/assets/game.json`

### Playable cases

The showcase demonstrates:

- baseline runner movement
- heavy slow reducing travel while preserving movement
- root freezing movement through move-block projection
- stun freezing movement and interrupting action through dual block projection
- silence denying cast startup without affecting movement
- stun interrupting an active hostile cast

The overlay and optional performer companion mod make the state visible while the acceptance path proves the gameplay behavior headlessly.

## Evidence

### Tests

Core and production evidence currently lives in:

- `src/Tests/GasTests/AttributeBindingTests.cs`
- `src/Tests/GasTests/OrderNavigationMoveRuntimeTests.cs`
- `src/Tests/GasTests/Production/ChampionControlShowcaseConfigTests.cs`
- `src/Tests/GasTests/Production/ChampionControlShowcasePlayableAcceptanceTests.cs`

### Acceptance artifacts

Generated artifacts:

- `artifacts/acceptance/champion-control-showcase/battle-report.md`
- `artifacts/acceptance/champion-control-showcase/trace.jsonl`
- `artifacts/acceptance/champion-control-showcase/path.mmd`
- `artifacts/acceptance/champion-control-showcase/screens/001.svg`
- `artifacts/acceptance/champion-control-showcase/screens/002.svg`
- `artifacts/acceptance/champion-control-showcase/screens/003.svg`
- `artifacts/acceptance/champion-control-showcase/screens/004.svg`
- `artifacts/acceptance/champion-control-showcase/screens/005.svg`
- `artifacts/acceptance/champion-control-showcase/screens/006.svg`
- `artifacts/acceptance/champion-control-showcase/screens/007.svg`
- `artifacts/acceptance/champion-control-showcase/screens/timeline.svg`

The acceptance test now drives the control cases through the marshal's real showcase ability slots and the standard cast-order planner, rather than publishing control effects directly into the queue.

## Extension Rules

When adding future control effects:

1. Reuse `GameplayControlState` if the effect blocks movement or action.
2. Keep scalar movement degradation on the `MoveSpeed` chain unless there is a proven mismatch.
3. Add reusable content to `CommonControlBuffsMod` when multiple mods can consume it.
4. Only add new Core channels when the behavior cannot be expressed through existing move/action block or ordinary attributes.
