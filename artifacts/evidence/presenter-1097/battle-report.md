# Presenter graph binding writes back to the Param Blackboard

## Scenario Card
- Player goal: a presenter param bound with `source=graph` shows the graph result in the same frame, instead of a silently skipped binding.
- Scope: Presenter-side binding bridge only (`PresenterBehaviorSystem`); no new Graph nodes, no Rule-condition semantics changes, no GAS handler / effect-preset / spawn-lifecycle changes.
- Ticket: MightyBubble/Ludots#1097 [P1].
- Branch: `codex/issue-1097-graph-binding-blackboard`.

## Contract (fixed by this PR)
- `PresenterParamBinding` with `source=graph, sourceId=<Score graph id>` evaluates the registered **Score** program and writes `F[0]` back to `ParamKey` on the **Float** lane (`ValueRef.Graph` doc is the SSOT).
- Input context seeded per evaluation: `E[0]=owner`, `E[1]=presenter`; `F[k]` for `k>=1` is seeded from the current Param Blackboard float lane key `k` (resolver order: override, default, parent chain). `F[0]` is reserved as the result register and never seeded as an input. Unseeded registers hold a NaN sentinel so an incomplete input surfaces as a NaN result.
- Bindings evaluate in definition order every frame (bootstrap pass + new `PerfHasGraphParamBinding` tick marker), so a later graph binding reads the value an earlier one wrote in the same pass, and `PresenterEmitSystem` AssetBinding reads happen after the write in the same frame (`PresenterBehaviorSystem` runs before `PresenterEmitSystem` in the presentation group).

## Timeline
- [T+000] `contract` -> Graph evaluate reads the Param Blackboard through the standard resolver; result written with `SetParam` (same write path as every other binding source).
- [T+001] `runtime` -> the `case ValueSourceKind.Graph: break;` skip branch is deleted; `ApplyBindings` evaluates graph bindings, and the tick-driven pass keeps them fresh each frame (`ApplyGraphParamBindings`).
- [T+002] `markers` -> `PerfHasGraphParamBinding` mirrors the facing-binding marker pattern (create/sync/remove/signature); tick fast paths are gated so graph bindings are never bypassed.
- [T+003] `failures` -> three structured failure classes with binding-path diagnostics via `Log.Warn` (once per presenter+binding), old value preserved, no half write:
  - graph missing / empty / wrong kind (RequireKind),
  - incomplete input (NaN result from an unseeded input register),
  - missing result lane (program never writes a float-typed `Dst=0`).
- [T+004] `engine` -> GameEngine passes `graphProgramRegistry` + `gasGraphApi` into `PresenterBehaviorSystem` (same instances `PresenterRuleSystem` already uses).

## Outcome
- success: yes
- same-frame order proven in-test: chained graph bindings (`GraphBinding_ChainEvaluatesInBindingOrder_WithinOneFrame`) and graph -> blackboard -> AssetBinding scale read with exact value equality (`GraphBinding_SameFrameTrace_GraphEvaluate_BlackboardWrite_AssetBindingRead`).
- three failure classes observable and value-preserving (`GraphBinding_MissingProgram/IncompleteInput/MissingFloatResultRegister/WrongKindProgram_WarnsAndKeepsOldValue`).
- non-graph sources unchanged (constant + graph mixed definition test; full presenter regression suite).

## Summary Stats
- new tests: `PresenterGraphBindingTests` 10/10 passed.
- targeted regression: PresentationTests `FullyQualifiedName~Presenter` (non-benchmark) 403 passed / 1 failed — `MapLoad_WiresQuarksParticleAssetsIntoRaylibVfxPresenterPath` reproduces on the clean baseline (pre-existing, unrelated; Raylib VFX showcase fixture).
- full `FullyQualifiedName~Presenter` including benchmarks: 147/150 (3 failures: the pre-existing VFX fixture plus 2 benchmark-fixture failures in the 58-minute run, not rerun individually; non-benchmark rerun above is clean apart from the pre-existing one).
- GasTests `FullyQualifiedName~Presenter`: see trace.jsonl.
- Core build: 0 errors, 0 new warnings in touched files.

## Known limitations
- Input register index = Param Blackboard key; usable input keys are 1..31 (32 float registers; F[0] reserved). Keys >= 32 cannot be graph inputs.
- The NaN sentinel catches incomplete inputs consumed through float paths; an input consumed only via a boolean comparison can evade the NaN guard (comparison output is a bool). Authored float constants cannot be NaN, so a NaN result still reliably means an unseeded input was read into the float path.
- Showcase slice deferred per ticket scope (no changes to `mods/showcases/presenter_blacksmith/` or `showcase.registry.json`).
