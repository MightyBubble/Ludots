# GAS Beam / AOE Manifestation

## 1. Scope

This document defines how Ludots extends the existing GAS, manifestation spawn, performer, and Raylib primitive pipelines to support visible, playable beam-like and wave-like showcase skills.

Target showcase families:

- penetrating beam
- sweeping beam
- sustained beam / channel beam
- spline beam
- diffusion wave
- ring pulse

The goal is not to introduce a separate beam runtime. The goal is to reuse the existing effect pipeline and give manifestations a reusable geometry payload that presentation adapters can render directly.

## 2. Task Decision Record

### 2.1 Judgment One

This is still a feature task on top of existing pipelines, not a brand-new runtime stack.

Existing infrastructure is sufficient for the authoritative side:

- `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs`
- `src/Core/Gameplay/GAS/BuiltinHandlers.cs`
- `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs`
- `src/Core/Gameplay/Spawning/Systems/ManifestationMotion2DSystem.cs`
- `src/Core/Presentation/Systems/PerformerEmitSystem.cs`
- `src/Core/Presentation/Rendering/PrimitiveDrawBuffer.cs`
- `src/Client/Ludots.Client.Raylib/Rendering/RaylibPrimitiveRenderer.cs`

Required work is an infrastructure-first extension inside those pipelines.

### 2.2 Reuse List

Reuse infrastructure:

- Registry: `AbilityDefinitionRegistry` for showcase ability registration
- Registry: `EffectTemplateRegistry` for authoritative beam / AOE effect definitions
- Registry: `PerformerDefinitionRegistry` for manifestation performer authoring
- Registry: `ComponentRegistry` for manifestation geometry JSON loading
- Pipeline: `EffectRequestQueue -> EffectProcessingLoopSystem -> GasPresentationEventBuffer`
- Pipeline: `PresentationCommandBuffer -> PerformerRuntimeSystem -> PerformerEmitSystem -> PrimitiveDrawBuffer`
- System: `ManifestationMotion2DSystem` for follow-parent, target-facing, and sweep rotation
- Mod: `ChampionSkillSandboxMod` as the playable showcase host

### 2.3 Gap Analysis

Existing GAS already supports the authoritative query shapes we need for most showcases:

- `Circle`
- `Cone`
- `Rectangle`
- `Line`
- `Ring`

Existing manifestation support already covers:

- template-backed spawned units
- parent linkage
- persistent on-spawn effects
- runtime follow / sweep motion

The real gap is presentation:

- manifestations do not carry structured beam / wave geometry
- performer output can only emit `GroundOverlay`, `Marker3D`, `WorldText`, or `WorldBar`
- Raylib primitive rendering does not know how to draw beam, spline, pulse, or disk-wave primitives

## 3. Core Design

### 3.1 Manifestation Geometry Is a Component

Add `ManifestationGeometry2D` as a gameplay-side component on spawned manifestations.

Responsibilities:

- declare the presentation geometry family for a manifestation
- carry reusable beam / wave parameters
- stay authorable from template JSON
- remain independent from adapter-specific rendering code

This component is presentation-facing data owned by gameplay manifestations. It is not a duplicate effect runtime.

### 3.2 Performer Emits a Manifestation Primitive Visual Kind

Add a new performer visual kind:

- `ManifestationPrimitive`

This kind reuses the existing performer evaluation and binding rules, but emits a richer primitive payload into `PrimitiveDrawBuffer`.

Resolution order stays the same:

- imperative override
- performer binding
- performer default
- manifestation geometry component values when the performer is attached to a manifestation entity

### 3.3 Primitive Payload Becomes Shape-Aware

`PrimitiveDrawItem` is extended so adapters can distinguish:

- regular mesh marker draw
- manifestation primitive draw

The same shared draw buffer remains the adapter contract. Existing static mesh and skinned lanes keep working.

### 3.4 Raylib Draws Beam / Wave Primitives Directly

Raylib adds dedicated rendering for manifestation primitives:

- beam segment strips
- spline beam polyline strips
- pulse ring loops
- disk-wave loops

Initial implementation favors deterministic line-strip rendering with width approximated by parallel offset strokes. This keeps the implementation debuggable and cross-platform while still making the showcases clearly visible.

## 4. Geometry Contract

### 4.1 Primitive Families

`ManifestationPrimitiveKind`:

- `Beam`
- `SplineBeam`
- `RingPulse`
- `DiskWave`

### 4.2 Shared Parameters

`ManifestationGeometry2D` carries reusable AOE-facing parameters:

- `LengthCm`
- `WidthCm`
- `EndWidthCm`
- `InnerRadiusCm`
- `OuterRadiusCm`
- `SweepAngleDeg`
- `SegmentCount`
- `ArcHeightCm`
- `ControlPoint0XCm`
- `ControlPoint0YCm`
- `ControlPoint1XCm`
- `ControlPoint1YCm`
- `PulseSpeed`
- `PulseAmplitudeCm`

These are intentionally generic enough to cover:

- straight line AOE beams
- widening beams
- expanding circles
- ring pulses
- curved spline beams

## 5. Showcase Mapping

### 5.1 Existing Showcase Upgrades

Upgrade current sandbox skills so they use visible manifestation primitives instead of only ground overlays:

- `Prismatic Beam`: instant penetrating beam visual
- `Guided Laser`: sustained target-facing beam manifestation
- `Gravity Well`: disk-wave / diffusion-wave manifestation
- `Cataclysm Ring`: ring pulse manifestation

### 5.2 New Showcase Coverage

Add one dedicated beam showcase champion so other developers can copy complete patterns:

- line pierce
- sweep beam
- spline lash
- pulse bloom

The authoritative side should stay composed from existing `Search`, `PeriodicSearch`, and `CreateUnit` presets. When a visual family is richer than the authoritative query shape, the manifestation visuals remain richer while damage stays driven by supported GAS search patterns.

## 6. Effect Authoring Guidance

Recommended effect patterns:

- one-shot penetrating beam:
  - `Search` with `shape = Line`
  - cue performer uses `ManifestationPrimitive`

- sustained beam:
  - `CreateUnit`
  - spawned template carries `ManifestationMotion2D` and `ManifestationGeometry2D`
  - spawned unit receives `PeriodicSearch` line effect

- sweep beam:
  - sustained beam pattern
  - `ManifestationMotion2D.SweepDegreesPerSecond` rotates facing over time

- ring pulse:
  - `CreateUnit`
  - `PeriodicSearch` with `shape = Ring` or `Circle`
  - manifestation performer renders pulse ring

- diffusion wave:
  - `CreateUnit`
  - `PeriodicSearch` with `shape = Circle`
  - manifestation performer renders expanding disk wave

- spline beam:
  - `CreateUnit`
  - authoritative damage composed from supported periodic searches
  - visual side uses `SplineBeam`

## 7. File Landing Plan

Core runtime:

- `src/Core/Gameplay/Spawning/ManifestationGeometry2D.cs`
- `src/Core/Config/ComponentRegistry.cs`
- `src/Core/Presentation/Performers/PerformerVisualKind.cs`
- `src/Core/Presentation/Performers/WellKnownPerformerParamKeys.cs`
- `src/Core/Presentation/Config/PerformerDefinitionConfigLoader.cs`
- `src/Core/Presentation/Rendering/PrimitiveDrawKind.cs`
- `src/Core/Presentation/Rendering/PrimitiveDrawItem.cs`
- `src/Core/Presentation/Rendering/PresentationVisualProxy.cs`
- `src/Core/Presentation/Rendering/PresentationVisualProxyEmitter.cs`
- `src/Core/Presentation/Systems/PerformerEmitSystem.cs`
- `src/Client/Ludots.Client.Raylib/Rendering/RaylibPrimitiveRenderer.cs`

Sandbox:

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Entities/templates.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Maps/champion_skill_sandbox.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/performers.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxVisualFeedback.cs`

Verification:

- `src/Tests/PresentationTests/PresentationFoundationTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxConfigTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxPlayableAcceptanceTests.cs`

## 8. Acceptance Expectations

The finished delivery must provide:

- headless acceptance coverage for the new showcase skills
- artifact output under `artifacts/acceptance/champion-skill-sandbox/`
- runtime screenshots captured from Raylib
- a playable sandbox map where the new manifestation abilities can be selected and cast directly

## 9. Non-Goals

This work does not introduce:

- a separate beam simulation subsystem
- adapter-owned gameplay logic
- a duplicate AOE authoring pipeline outside GAS
- fallback compatibility shims for old beam-specific hacks
