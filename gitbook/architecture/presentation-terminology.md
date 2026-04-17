# Presentation Terminology

This document defines the canonical presentation terms used by Core, config authoring, tests, and showcase mods.

The goal is simple:

- shared asset categories use one stable name each
- runtime packets use one stable name each
- fixture or acceptance sample names never become asset-category names

## Canonical Terms

| Term | Meaning | Not this |
| --- | --- | --- |
| `Prefab` | An authored presentation asset composed from one or more visual parts. | A runtime instance, a performer, or a behavior. |
| `Prefab visual part kind` | The authored part category inside a prefab. The current Core-owned kinds are `Mesh`, `Decal`, `Vfx`, and `Surface`. | A sample asset name such as `cue_marker`. |
| `Prefab finalized visual` | A finalized runtime visual packet emitted by prefab finalization after grounding / transform resolution. | The authored prefab itself. |
| `Presentation behavior` | A Core-owned state-to-prefab mapping contract for semantic presentation states. | A performer definition, a prefab, or a one-shot request. |
| `Performer` | A presentation observer/runtime definition that reacts to events and emits presentation requests. | An asset category or a prefab part kind. |
| `Presentation request` | The adapter-neutral runtime output gate consumed by the flush step before adapter-facing buffers. | A config asset or a direct adapter instruction. |
| `Fixture prefab` | A mod-local sample prefab used only by a fixture, acceptance test, benchmark, or showcase. | A shared asset category or shared built-in contract. |

## Naming Rules

### 1. Asset categories use type names

Use category names for categories only:

- `Prefab`
- `Presentation behavior`
- `Performer`
- `Presentation request`
- `Mesh`
- `Decal`
- `Vfx`
- `Surface`

Do not use sample names like `cue_marker`, `typed cue`, `camera acceptance cue`, or similar phrases as category labels.

### 2. Fixture assets must look like fixtures

Fixture-only assets must be visibly scoped in their ids and comments.

Good examples:

- `camera_acceptance_fixture_cue_prefab`
- `performance_visualization_large_lane`

Bad examples:

- `typed_cue`
- `special_prefab_kind`
- `acceptance_visual_type`

### 3. One thing, one name

When referring to the same concept across code, config, tests, and docs, reuse the same canonical term.

Examples:

- Use `Prefab visual part kind`, not a mix of `visual kind`, `part kind`, `typed visual kind`, and `prefab leaf type`.
- Use `Presentation behavior`, not a mix of `semantic visual`, `state visual`, `behavior prefab`, and `presentation state asset`.
- Use `Prefab finalized visual`, not a mix of `finalized leaf`, `resolved prefab visual`, and `adapter visual payload` when the same packet is meant.

### 4. Shared built-ins and fixture assets are different things

Shared built-ins may have stable asset ids such as `cue_marker`, but those ids are concrete assets, not category names.

Fixture assets must stay mod-local and should not redefine the shared vocabulary of the presentation layer.

## Current Shared Vocabulary

The current shared presentation vocabulary for prefab assets is:

- asset type: `Prefab`
- part kinds: `Mesh`, `Decal`, `Vfx`, `Surface`
- runtime output: `Prefab finalized visual`

The current shared presentation vocabulary for orchestration is:

- event observer/runtime: `Performer`
- state-to-prefab semantic contract: `Presentation behavior`
- adapter-neutral output gate: `Presentation request`

## Scope Guard

If a new name is only needed by:

- a fixture
- an acceptance scenario
- a showcase mod
- a benchmark lane

then it must stay scoped to that mod or scenario and must not be introduced as a new shared asset category.
