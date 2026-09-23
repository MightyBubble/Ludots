# Entity Query Tactics Production Benchmark

## Run Metadata
- command: `dotnet test src/Tests/GasTests/GasTests.csproj --filter EntityQueryTactics_ProductionBenchmark_WritesReport --no-restore`
- runtime: `.NET 9.0.14`
- os: `Microsoft Windows 10.0.26220`
- generated UTC: `2026-09-22T14:18:36.3301920Z`
- preset: `entity_query_tactics_raylib`
- plan fingerprint: `24e269c848341eb7119e3715b37029a18a57c757432c7d0a22a4a70f59da046d`
- ordered mods: `LudotsCoreMod -> CoreInputMod -> CameraProfilesMod -> NarrativeFrontendMod -> EntityQueryTacticsShowcaseMod`
- graph ids: `entityquery.tactics.graph.selectedFriendliesFromUiBox, entityquery.tactics.graph.hostileThreatBoard, entityquery.tactics.graph.formationCache`
- graph node counts: selected `14`, hostile `15`, formation `13`
- graph output bindings: `16`
- asset hash `EntityQueryTacticsShowcaseConfig.json`: `71F37155C57CF6355FEB14700862BD6A2D640E098A923791B26B212E5A06EDE8`
- asset hash `Frontend/entity_query_tactics_frontend.json`: `ACDFAA8B359B367A7A68271F7D4560A6286EF3B4797592233D8A9A7ECE955724`
- asset hash `Presentation/presenters.json`: `17AC9AB3DE2D0CB70719AAEB0392666C708782F75CFE75CB8C58AE58CB89317B`
- asset hash `Camera/virtual_cameras.json`: `795894774C91588529A07134D7DBBFFD2C423D8D17D9BA369B4AAD90C617B55B`
- asset hash `GAS/graphs.json`: `B67C8C2952AF5F1A1527505CAD63CAD85F9B73FE18B429BAEEB38CBBCC95D338`
- asset hash `GAS/attribute_constraints.json`: `6E162AAD0B8C570B022D38EAF992A2D0035FC248C47071DACB32C2A1BC193D48`
- asset hash `GAS/tag_rules.json`: `D9C0811F959F2C0810757B6467DD2AF51DBBBC21FAF7F413EE7A52CC1466E0C9`
- asset hash `Relationships/catalog.json`: `8F194059E734D01D9F119FE1C3E5BEBE2FB6217AFF6E88C37CEDB21B8A2EA3FA`
- asset hash `Entities/templates.json`: `4B9A410DB6B85AF197188BC014FC3FECCD6B77D0676069986B8AFC72D2ED80E8`
- asset hash `Maps/entity_query_tactics_showcase.json`: `73CC99147D3399D5FBA0BE3B548CA6215CDD864E8104F6A4D21CC6B5C6D313AB`
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
| GraphReturnWriter execute x3 stable inputs | 20000 | 511.529 | 25.576 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.selectedFriendliesFromUiBox` only | 20000 | 149.342 | 7.467 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.hostileThreatBoard` only | 20000 | 200.068 | 10.003 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.formationCache` only | 20000 | 140.039 | 7.002 | 0 |
| Retained diff execute x3 stable inputs | 2000 | 49.686 | 24.843 | 0 |
| Relationship AddMetric + graph execute x3 | 1000 | 26.581 | 26.581 | 0 |
- stable allocation sample attempts: graph x3 `1`, single graphs `entityquery.tactics.graph.selectedFriendliesFromUiBox:1, entityquery.tactics.graph.hostileThreatBoard:1, entityquery.tactics.graph.formationCache:1`, retained diff `1`, pressure `1`

## Production Tick Loop
| path | frames | action frames | total ms | median ms | p95 ms | max ms | allocated bytes |
|---|---:|---:|---:|---:|---:|---:|---:|
| PlayerInputHandler + GameEngine.Tick + showcase systems | 360 | 150 | 188.914 | 0.150 | 1.361 | 4.080 | 117626416 |
- production pressure summary: `entityquery.summary.threat.max` `95` -> `605` during the tick loop.

## Retained Diff
- stable formation revisions: `2000/2000`
- stable probe before: rev `2`, sig `0x75D8EF49447514A6`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- stable probe after: rev `2`, sig `0x75D8EF49447514A6`, count `4`, names `Aegis Captain, Spear One, Spear Two, Field Medic`
- rotation input: `entityquery.collection.formation.primary` rev `3` -> `4`, sig `0x2DD21B22EF50236B` -> `0xE8AFA1E35C514BA0`
- rotation output: `entityquery.collection.graph.formationCache` rev `2` -> `2`, sig `0x75D8EF49447514A6` -> `0x75D8EF49447514A6`
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
