# Scenario Card: entity-query-tactics-showcase

## Intent
- Player goal: drag-select allies, run query graphs, inspect hostile relation threat, rotate formation cache, probe retained diff, and mutate pressure under a production mod path.
- Gameplay domain: SelectionRuntime, UI acquisition collection, formal selection mirror, EntityCollectionStore, GraphReturnWriter, EntitySetQueryRuntime, RelationshipRuntime, tags, attrs, templates, sorting, extremes, and aggregates.

## Determinism Inputs
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`
- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic mouse/keyboard backend.
- Clock profile: fixed `1/60s` headless `GameEngine.Tick()`.
- Pressure scale: `1541` allies, `484` enemies, `1` objectives, `2016` runtime-generated actors, Graph VM target capacity `2048`.

## Timeline
- [T+001] Loaded production Entity Query Tactics map through ConfigPipeline; UI mounted and graph outputs were initialized by the mod system.
- [T+002] Config-driven UI acquisition wrote `1541` friendlies into EntityCollectionStore and left formal SelectionRuntime empty.
- [T+003] Configured commit action copied the UI acquisition collection into SelectionRuntime live primary and refreshed the formation command source.
- [T+004] GraphReturnWriter materialized `entityquery.tactics.graph.selectedFriendliesFromUiBox` from `entityquery.collection.ui.box` with graph-defined team/template/tag/attr filters, sorting, aggregate, and extreme summaries.
- [T+005] `entityquery.tactics.graph.hostileThreatBoard` used real RelationshipRuntime `TacticalIntel` metric/flag filters, sorted priority hostiles, and aggregated threat sum/avg/max.
- [T+006] `entityquery.tactics.graph.formationCache` rotated `entityquery.collection.formation.primary` and graph-defined tag exclusions ran before max/min summaries.
- [T+007] Cache probe reran graph materialization with stable inputs; retained diff kept the formation result revision unchanged.
- [T+008] Pressure pulse mutated RelationshipRuntime only; rerun graph summaries reflected `Threat` 95->112.

## Outcome
- success: yes
- verdict: selected `Blue Line 0024, Blue Line 0048, Blue Line 0072, Blue Line 0096, Blue Line 0120, Blue Line 0144, Blue Line 0168, Blue Line 0192, +1,532 more (1,540 rows)`, formation `Blue Line 0024, Blue Line 0048, Blue Line 0072, Blue Line 0096, Blue Line 0120, Blue Line 0144, Blue Line 0168, Blue Line 0192, +1,532 more (1,540 rows)`, hostile `Siege Runner, Crimson Siege 0039, Crimson Siege 0117, Crimson Siege 0195, Crimson Siege 0273, Crimson Siege 0351, Crimson Siege 0429, Crimson Siege 0077, +235 more (243 rows)` all came from retained graph materializations.

## Summary Stats
- snapshots captured: `8`
- median headless tick: `8.752ms`
- p95 headless tick: `276.185ms`
- max headless tick: `665.100ms`
- tick note: acceptance timings include map startup, UI sync, evidence capture staging, and action frames; the dedicated production pressure loop is reported in the benchmark artifact.
- final selected count: `1540`
- final threat max: `112`
- final formation count: `1540`
- visible battlefield dots: `2026` actors drawn in the artifact overlay
- final pressure corpus: ui box `1541`, selected graph `1540`, hostile relation graph `243`, total actors `2026`
- final revisions: ui `1`, formal `2`, formation `2`, hostile `1`
- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `CurrentSelectionApplySystem`, `SelectionRuntime`, `EntityCollectionStore`, `GraphReturnWriter`, `EntitySetQueryRuntime`, `RelationshipRuntime`, `NarrativeFrontendService`
