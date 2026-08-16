# Entity Query Tactics Production Benchmark

## Run Metadata
- command: `dotnet test src/Tests/GasTests/GasTests.csproj --filter EntityQueryTactics_ProductionBenchmark_WritesReport --no-restore`
- runtime: `.NET 8.0.25`
- os: `Microsoft Windows 10.0.26220`
- generated UTC: `2026-08-16T17:39:39.1779594Z`
- preset: `entity_query_tactics_raylib`
- plan fingerprint: `8e69549f3ab028744f7191eaa08fbd4f028e9d893612ba6c0231738a0ec51dd1`
- ordered mods: `LudotsCoreMod -> CoreInputMod -> CameraProfilesMod -> NarrativeFrontendMod -> EntityQueryTacticsShowcaseMod`
- graph ids: `entityquery.tactics.graph.selectedFriendliesFromUiBox, entityquery.tactics.graph.hostileThreatBoard, entityquery.tactics.graph.formationCache`
- graph node counts: selected `14`, hostile `15`, formation `13`
- graph output bindings: `16`
- asset hash `EntityQueryTacticsShowcaseConfig.json`: `71F37155C57CF6355FEB14700862BD6A2D640E098A923791B26B212E5A06EDE8`
- asset hash `Frontend/entity_query_tactics_frontend.json`: `381869E404BF484BB161C92B2753D61D9D5882D1584AEE04D4E86E332BC1A7CE`
- asset hash `Presentation/presenters.json`: `AE05C7F883FB5C05174BC8931931DE5459D1B00A3ED30D486895740D3B6A049E`
- asset hash `Camera/virtual_cameras.json`: `795894774C91588529A07134D7DBBFFD2C423D8D17D9BA369B4AAD90C617B55B`
- asset hash `GAS/graphs.json`: `20C5F2115CEE44960372A7D98A1CFDF6EA85FC3BA15D106E19E84793CEBE266A`
- asset hash `GAS/attribute_constraints.json`: `6E162AAD0B8C570B022D38EAF992A2D0035FC248C47071DACB32C2A1BC193D48`
- asset hash `GAS/tag_rules.json`: `D9C0811F959F2C0810757B6467DD2AF51DBBBC21FAF7F413EE7A52CC1466E0C9`
- asset hash `Relationships/catalog.json`: `9375366CA21783040A16D5026F249319DDB29105A56A15CE008ACB9377D05B1E`
- asset hash `Entities/templates.json`: `333C849BD5EDCE16BD00035B6E825F7761B3548C3B41C875082B50585A453062`
- asset hash `Maps/entity_query_tactics_showcase.json`: `4228F72588388208AE840139108E5B3C867D35863901B06339DCFE0F0146CC38`
- asset hash `Input/default_input.json`: `A04F59F28AD7C6A88F49E3564F1DE2C43ACFDE4359D61B98AFE7231A76E87EED`

## Production Chain
- map: `entity_query_tactics_showcase`
- mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`
- graphs: `entityquery.tactics.graph.selectedFriendliesFromUiBox`, `entityquery.tactics.graph.hostileThreatBoard`, `entityquery.tactics.graph.formationCache`
- collections: `entityquery.collection.ui.box`, `entityquery.collection.command.source.mirror`, `entityquery.collection.formation.primary`, `entityquery.collection.graph.formationCache`
- relationship type: `TacticalIntel`
- pressure metric: `Threat`
- warmup graph executions: `8000` iterations plus `5000` post-GC stabilization iterations before allocation timing

## Hot Path Measurements
| path | iterations | total ms | per iteration us | allocated bytes |
|---|---:|---:|---:|---:|
| GraphReturnWriter execute x3 stable inputs | 20000 | 528.753 | 26.438 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.selectedFriendliesFromUiBox` only | 20000 | 215.388 | 10.769 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.hostileThreatBoard` only | 20000 | 219.446 | 10.972 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.formationCache` only | 20000 | 179.670 | 8.983 | 0 |
| Retained diff execute x3 stable inputs | 2000 | 57.316 | 28.658 | 0 |
| Relationship AddMetric + graph execute x3 | 1000 | 26.693 | 26.693 | 0 |
- stable allocation sample attempts: graph x3 `1`, single graphs `entityquery.tactics.graph.selectedFriendliesFromUiBox:1, entityquery.tactics.graph.hostileThreatBoard:1, entityquery.tactics.graph.formationCache:1`, retained diff `1`, pressure `1`

## Production Tick Loop
| path | frames | action frames | total ms | median ms | p95 ms | max ms | allocated bytes |
|---|---:|---:|---:|---:|---:|---:|---:|
| PlayerInputHandler + GameEngine.Tick + showcase systems | 360 | 150 | 276.756 | 0.273 | 2.256 | 6.460 | 119505576 |
- production pressure summary: `entityquery.summary.threat.max` `95` -> `605` during the tick loop.

## Retained Diff
- stable formation revisions: `2000/2000`
- stable probe before: rev `2`, sig `0xA4AB60DAA05C58FA`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- stable probe after: rev `2`, sig `0xA4AB60DAA05C58FA`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- rotation input: `entityquery.collection.formation.primary` rev `3` -> `4`, sig `0x6C08F8856DFB9CA7` -> `0xE0DBE7BA16A23314`
- rotation output: `entityquery.collection.graph.formationCache` rev `2` -> `2`, sig `0xA4AB60DAA05C58FA` -> `0xA4AB60DAA05C58FA`
- expected: stable inputs keep `entityquery.collection.graph.formationCache` revision unchanged; order-only source rotation is normalized by graph sorting and retained output signature.

## Relationship Pressure Buffer
- change records: `0` -> `1000`
- change buffer capacity: `2048` -> `2048`
- change buffer resize delta: `0`

## Architecture Notes
- C# systems and visual graph ops share the same runtime APIs: `GraphReturnWriter -> GasGraphRuntimeApi -> EntitySetQueryRuntime / RelationshipRuntime`.
- The showcase is configured through mod assets and loaded by `ConfigPipeline`; the benchmark does not create a parallel query, selection, or relationship system.
- Hot path allocation counts use current-thread `GC.GetAllocatedBytesForCurrentThread()` after warmup and measured zero-allocation stabilization; setup, JSON loading, UI screenshots, and report writing are outside the asserted allocation windows.
- Full tick loop allocation is reported for realism, not asserted as 0Alloc, because it includes input, UI, presentation text, and showcase state publication.
