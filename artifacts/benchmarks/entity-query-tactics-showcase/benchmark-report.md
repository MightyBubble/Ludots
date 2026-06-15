# Entity Query Tactics Production Benchmark

## Run Metadata
- command: `dotnet test src/Tests/GasTests/GasTests.csproj --filter EntityQueryTactics_ProductionBenchmark_WritesReport --no-restore`
- runtime: `.NET 8.0.25`
- os: `Microsoft Windows 10.0.26220`
- generated UTC: `2026-06-15T23:35:07.7479143Z`
- preset: `entity_query_tactics_raylib`
- plan fingerprint: `f7012ed15c194e81c8bb566c3d3bf95f5668e352b7e95f88b874ddb562a1e427`
- ordered mods: `LudotsCoreMod -> CoreInputMod -> CameraProfilesMod -> NarrativeFrontendMod -> EntityQueryTacticsShowcaseMod`
- graph ids: `entityquery.tactics.graph.selectedFriendliesFromUiBox, entityquery.tactics.graph.hostileThreatBoard, entityquery.tactics.graph.formationCache`
- graph node counts: selected `14`, hostile `15`, formation `13`
- graph output bindings: `16`
- asset hash `EntityQueryTacticsShowcaseConfig.json`: `A4B8F77B6DB1D250068BD59681860E520413FA26DB211357486CC2CCC13BB5A6`
- asset hash `Frontend/entity_query_tactics_frontend.json`: `381869E404BF484BB161C92B2753D61D9D5882D1584AEE04D4E86E332BC1A7CE`
- asset hash `Presentation/performers.json`: `FB039931137270C628A3F0546DD0C4DB2F193FFA7512D60F9AD2885F8E006A55`
- asset hash `Configs/Camera/virtual_cameras.json`: `795894774C91588529A07134D7DBBFFD2C423D8D17D9BA369B4AAD90C617B55B`
- asset hash `GAS/graphs.json`: `08B1287E2E3DD8E1CA004C48C4F28FDBC50602DD7264D18066A1BE9291EE51CF`
- asset hash `GAS/attribute_constraints.json`: `6E162AAD0B8C570B022D38EAF992A2D0035FC248C47071DACB32C2A1BC193D48`
- asset hash `GAS/tag_rules.json`: `D9C0811F959F2C0810757B6467DD2AF51DBBBC21FAF7F413EE7A52CC1466E0C9`
- asset hash `Relationships/catalog.json`: `793C09977E2C15DDD2B9CD7C892065647688B8DBC0DA567BFB388F2309AFE144`
- asset hash `Entities/templates.json`: `363E2ED3C5E365EFBC68D9B014B47A9A103FCEC83160C00D41BEB2E24FEF3A97`
- asset hash `Maps/entity_query_tactics_showcase.json`: `9192CCF009F90B14398042A6EB33094E63BE8EC522075D0A55C8EE33ED2C0930`
- asset hash `Input/default_input.json`: `A04F59F28AD7C6A88F49E3564F1DE2C43ACFDE4359D61B98AFE7231A76E87EED`

## Production Chain
- map: `entity_query_tactics_showcase`
- mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`
- graphs: `entityquery.tactics.graph.selectedFriendliesFromUiBox`, `entityquery.tactics.graph.hostileThreatBoard`, `entityquery.tactics.graph.formationCache`
- collections: `entityquery.collection.ui.box`, `entityquery.collection.selection.live.primary`, `entityquery.collection.formation.primary`, `entityquery.collection.graph.formationCache`
- relationship type: `TacticalIntel`
- pressure metric: `Threat`
- warmup graph executions: `8000` iterations plus `5000` post-GC stabilization iterations before allocation timing

## Hot Path Measurements
| path | iterations | total ms | per iteration us | allocated bytes |
|---|---:|---:|---:|---:|
| GraphReturnWriter execute x3 stable inputs | 20000 | 868.287 | 43.414 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.selectedFriendliesFromUiBox` only | 20000 | 259.319 | 12.966 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.hostileThreatBoard` only | 20000 | 354.644 | 17.732 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.formationCache` only | 20000 | 167.352 | 8.368 | 0 |
| Retained diff execute x3 stable inputs | 2000 | 50.806 | 25.403 | 0 |
| Relationship AddMetric + graph execute x3 | 1000 | 26.169 | 26.169 | 0 |
- stable allocation sample attempts: graph x3 `2`, single graphs `entityquery.tactics.graph.selectedFriendliesFromUiBox:1, entityquery.tactics.graph.hostileThreatBoard:1, entityquery.tactics.graph.formationCache:1`, retained diff `1`, pressure `1`

## Production Tick Loop
| path | frames | action frames | total ms | median ms | p95 ms | max ms | allocated bytes |
|---|---:|---:|---:|---:|---:|---:|---:|
| PlayerInputHandler + GameEngine.Tick + showcase systems | 360 | 150 | 203.480 | 0.437 | 1.272 | 3.607 | 59475688 |
- production pressure summary: `entityquery.summary.threat.max` `95` -> `605` during the tick loop.

## Retained Diff
- stable formation revisions: `2000/2000`
- stable probe before: rev `2`, sig `0xAFC499CA98E72E0B`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- stable probe after: rev `2`, sig `0xAFC499CA98E72E0B`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- rotation input: `entityquery.collection.formation.primary` rev `3` -> `4`, sig `0x126CA9DAA869BF56` -> `0x2AFD56FE61291399`
- rotation output: `entityquery.collection.graph.formationCache` rev `2` -> `2`, sig `0xAFC499CA98E72E0B` -> `0xAFC499CA98E72E0B`
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
