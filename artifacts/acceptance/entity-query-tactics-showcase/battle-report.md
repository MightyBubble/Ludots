# Scenario Card: entity-query-tactics-showcase

## Intent
- Player goal: drag-select allies, run query graphs, inspect hostile relation threat, rotate formation cache, probe retained diff, and mutate pressure under a production mod path.
- Gameplay domain: command-source collection, UI acquisition collection, formation collection, EntityCollectionStore, GraphReturnWriter, EntitySetQueryRuntime, RelationshipRuntime, tags, attrs, templates, sorting, extremes, and aggregates.

## Determinism Inputs
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`
- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic mouse/keyboard backend.
- Clock profile: fixed `1/60s` headless `GameEngine.Tick()`.

## Timeline
- [T+001] Loaded production Entity Query Tactics map through ConfigPipeline; UI mounted and graph outputs were initialized by the mod system.
- [T+002] Player dragged a friendly box; CommandSourceAcquisition wrote both the UI acquisition collection and the authoritative command source.
- [T+003] Configured commit action confirmed the command source and refreshed the formation collection.
- [T+004] GraphReturnWriter materialized `entityquery.tactics.graph.selectedFriendliesFromUiBox` from `entityquery.collection.ui.box` with graph-defined team/template/tag/attr filters, sorting, aggregate, and extreme summaries.
- [T+005] `entityquery.tactics.graph.hostileThreatBoard` used real RelationshipRuntime `TacticalIntel` metric/flag filters, sorted priority hostiles, and aggregated threat sum/avg/max.
- [T+006] `entityquery.tactics.graph.formationCache` rotated `entityquery.collection.formation.primary` and graph-defined tag exclusions ran before max/min summaries.
- [T+007] Cache probe reran graph materialization with stable inputs; retained diff kept the formation result revision unchanged.
- [T+008] Pressure pulse mutated RelationshipRuntime only; rerun graph summaries reflected `Threat` 95->112.

## Outcome
- success: yes
- verdict: selected `Aegis Captain, Spear One, Spear Two, Field Medic`, formation `Aegis Captain, Spear One, Spear Two, Field Medic`, hostile `Siege Runner, Crimson Captain, Raid Alpha` all came from retained graph materializations.

## Summary Stats
- snapshots captured: `8`
- median headless tick: `0.606ms`
- p95 headless tick: `6.000ms`
- max headless tick: `36.405ms`
- tick note: acceptance timings include map startup, UI sync, evidence capture staging, and action frames; the dedicated production pressure loop is reported in the benchmark artifact.
- final selected count: `4`
- final threat max: `112`
- final formation count: `4`
- final revisions: ui `1`, command source `2`, formation `2`, hostile `1`
- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `CommandSourceAcquisitionSystem`, `EntityCollectionStore`, `GraphReturnWriter`, `EntitySetQueryRuntime`, `RelationshipRuntime`, `NarrativeFrontendService`
