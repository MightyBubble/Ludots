# Entity Query Tactics Production Benchmark

## Run Metadata
- command: `dotnet test src/Tests/GasTests/GasTests.csproj --filter EntityQueryTactics_ProductionBenchmark_WritesReport --no-restore`
- runtime: `.NET 8.0.25`
- os: `Microsoft Windows 10.0.26220`
- generated UTC: `2026-06-16T07:36:20.8909991Z`
- preset: `entity_query_tactics_raylib`
- plan fingerprint: `ed9554a9a0289dd40699821108375c938bec373c08feb34150e3aa414697737e`
- ordered mods: `LudotsCoreMod -> CoreInputMod -> CameraProfilesMod -> NarrativeFrontendMod -> EntityQueryTacticsShowcaseMod`
- graph ids: `entityquery.tactics.graph.selectedFriendliesFromUiBox, entityquery.tactics.graph.hostileThreatBoard, entityquery.tactics.graph.formationCache`
- graph node counts: selected `14`, hostile `15`, formation `13`
- graph output bindings: `16`
- Graph VM target capacity: `2048`
- asset hash `EntityQueryTacticsShowcaseConfig.json`: `9126B75794716FF4CD98DDA5E1CC66267410291C715D67AC2B7769BB61E38837`
- asset hash `Frontend/entity_query_tactics_frontend.json`: `0D8A484B538465D9356B45010F685CF797303B318B27A6A6F4C6A19462ED453C`
- asset hash `Presentation/performers.json`: `AD47556AD5C5BB8F2140ADF4F2A38CC66940BE9EEB583B7B06B25E082164ADA1`
- asset hash `Configs/Camera/virtual_cameras.json`: `A394C1940A75BF994EF5DA9BDD644F6E34F3F2C7EB6E77342F590184F8003A42`
- asset hash `GAS/graphs.json`: `115144C891C62E3AA7DE897D7AB7AD48649E18432506921EBEAEDFE6E250CB94`
- asset hash `GAS/attribute_constraints.json`: `8FACCE69B83DB37922B7EE9A703D5EAE6CE258F66E9E4833E0F84C1E616E7B95`
- asset hash `GAS/tag_rules.json`: `966BC16498D69929B403F54DF38E619067A4A29D7CD781D13CAE1C32526A1F57`
- asset hash `Relationships/catalog.json`: `C615A16FA58DDECE1BE42E3600F3F0146778780E0FB1730C8E27EEF8AFA2CDA0`
- asset hash `Entities/templates.json`: `167B74C166F48989D9FFEEEA3164904EF867718823C3884E7D26C2026DA7F4BE`
- asset hash `Maps/entity_query_tactics_showcase.json`: `7FAD2260862621FCE74AC440A9DBA92CCD5EBD6BCBABF95246439011CB91CC50`
- asset hash `Input/default_input.json`: `F26D92407B8BD7189F6C264493096482303A8556E4AD5E34AFE39176D8AAB9BD`

## Pressure Scale
- configured actors: allies `1541`, enemies `484`, objectives `1`, total `2026`
- runtime-generated actors: allies `1536`, enemies `480`, objectives `0`, total `2016`
- graph materialized rows: selected `1540`, hostile relation `243`, formation stable `1540`
- selected/formation pressure stays below Graph VM target capacity: `1541` / `2048`

## Production Chain
- map: `entity_query_tactics_showcase`
- mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`
- graphs: `entityquery.tactics.graph.selectedFriendliesFromUiBox`, `entityquery.tactics.graph.hostileThreatBoard`, `entityquery.tactics.graph.formationCache`
- collections: `entityquery.collection.ui.box`, `entityquery.collection.selection.live.primary`, `entityquery.collection.formation.primary`, `entityquery.collection.graph.formationCache`
- relationship type: `TacticalIntel`
- pressure metric: `Threat`
- warmup graph executions: `64` iterations plus `32` post-GC stabilization iterations before allocation timing

## Hot Path Measurements
| path | iterations | total ms | per iteration us | allocated bytes |
|---|---:|---:|---:|---:|
| GraphReturnWriter execute x3 stable inputs | 240 | 985.032 | 4104.300 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.selectedFriendliesFromUiBox` only | 240 | 473.585 | 1973.272 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.hostileThreatBoard` only | 240 | 122.314 | 509.642 | 0 |
| GraphReturnWriter execute `entityquery.tactics.graph.formationCache` only | 240 | 524.660 | 2186.085 | 0 |
| Retained diff execute x3 stable inputs | 240 | 1045.816 | 4357.566 | 0 |
| Relationship AddMetric + graph execute x3 | 120 | 470.320 | 3919.337 | 0 |
- stable allocation sample attempts: graph x3 `1`, single graphs `entityquery.tactics.graph.selectedFriendliesFromUiBox:1, entityquery.tactics.graph.hostileThreatBoard:1, entityquery.tactics.graph.formationCache:1`, retained diff `1`, pressure `1`

## Production Tick Loop
| path | frames | action frames | total ms | median ms | p95 ms | max ms | allocated bytes |
|---|---:|---:|---:|---:|---:|---:|---:|
| PlayerInputHandler + GameEngine.Tick + showcase systems | 180 | 75 | 1626.075 | 6.010 | 22.367 | 66.231 | 30761856 |
- production pressure summary: `entityquery.summary.threat.max` `95` -> `350` during the tick loop.

## Retained Diff
- stable formation revisions: `240/240`
- stable probe before: rev `2`, sig `0xA96DC88B5B0178B`, count `1540`, names `Blue Line 0024, Blue Line 0048, Blue Line 0072, Blue Line 0096, Blue Line 0120, Blue Line 0144, Blue Line 0168, Blue Line 0192, +1,532 more (1,540 rows)`
- stable probe after: rev `2`, sig `0xA96DC88B5B0178B`, count `1540`, names `Blue Line 0024, Blue Line 0048, Blue Line 0072, Blue Line 0096, Blue Line 0120, Blue Line 0144, Blue Line 0168, Blue Line 0192, +1,532 more (1,540 rows)`
- rotation input: `entityquery.collection.formation.primary` rev `3` -> `4`, sig `0xE66A37ABC0F6BEB2` -> `0x917A4DC3B62A827F`
- rotation output: `entityquery.collection.graph.formationCache` rev `2` -> `2`, sig `0xA96DC88B5B0178B` -> `0xA96DC88B5B0178B`
- expected: stable inputs keep `entityquery.collection.graph.formationCache` revision unchanged; order-only source rotation is normalized by graph sorting and retained output signature.

## Relationship Pressure Buffer
- change records: `0` -> `120`
- change buffer capacity: `2048` -> `2048`
- change buffer resize delta: `0`

## Architecture Notes
- C# systems and visual graph ops share the same runtime APIs: `GraphReturnWriter -> GasGraphRuntimeApi -> EntitySetQueryRuntime / RelationshipRuntime`.
- The showcase is configured through mod assets and loaded by `ConfigPipeline`; the benchmark does not create a parallel query, selection, or relationship system.
- Hot path allocation counts use current-thread `GC.GetAllocatedBytesForCurrentThread()` after warmup and measured zero-allocation stabilization; setup, JSON loading, UI screenshots, and report writing are outside the asserted allocation windows.
- Full tick loop allocation is reported for realism, not asserted as 0Alloc, because it includes input, UI, presentation text, and showcase state publication.
