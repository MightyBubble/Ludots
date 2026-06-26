# NavBakeContext And Unified Bake Service

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Primary subissue: [NAV-5 #287](https://github.com/MightyBubble/Ludots/issues/287). Runtime incremental follow-up: [NAV-10 #304](https://github.com/MightyBubble/Ludots/issues/304). Related vocabulary: [NAV-0 #282](https://github.com/MightyBubble/Ludots/issues/282), [NAV-2 #284](https://github.com/MightyBubble/Ludots/issues/284), [NAV-3 #285](https://github.com/MightyBubble/Ludots/issues/285), [NAV-4 #286](https://github.com/MightyBubble/Ludots/issues/286). Bake planning: [Nav Bake Budget and Estimation](nav-bake-budget-and-estimation.md). Authoring toolchain: [Navmesh Authoring Bake Toolchain](navmesh-authoring-bake-toolchain.md).

## Background

Before NAV-5, navigation bake had multiple entry points and duplicated parameter flows:

| Entry | Old flow | Problem |
|---|---|---|
| CLI `nav bake` | Read `.vtxm` and called `NavTileBuilder` directly | Bypassed unified profile and obstacle config |
| CLI `nav bake-react` | Converted React `map_data.bin` then baked locally | Duplicated target selection and parallel loops |
| CLI `nav bake-recast-react` | Loaded `Navigation/navmesh.json` then ran Recast loops locally | Duplicated editor Bridge logic |
| Bridge `/api/nav/bake-react` | Form fields drove `BakePipeline` directly | CDT could hide failures behind old grid fallback |
| Bridge `/api/nav/bake-recast-react` | Form fields drove `RecastNavTileBaker` and wrote disk output | Diverged from CLI parameters and output policy |

The result was drift between headed and headless bakes, path strings could degrade into filesystem-only contracts, and algorithm failures could be masked by fallback output.

## Target

`NavBakeContext` is the single bake request object and `NavBakeService` is the single execution entry. CLI and editor Bridge adapters only translate command or multipart input into the same context.

Before running a large bake, use the budget model in [Nav Bake Budget and Estimation](nav-bake-budget-and-estimation.md). The estimate must be built from the same terrain, obstacle, layer, profile, target, mode, and algorithm inputs that `NavBakeContext` will use for the real bake.

In scope:

- `NavBakeContext` carries map/profile/layer/obstacle/terrain/targets/build config/source URI.
- `NavBakeService` dispatches to concrete `INavBakeAlgorithm` adapters.
- Offline full bake defaults to `recast`; `cdt` is an explicit algorithm adapter.
- Runtime incremental local rebuild uses `runtime-incremental` + `cdt`.
- `Navigation/navmesh.json` must explicitly define `mode`, `algorithm`, and `runtimeIncremental`; casing is strict.
- CDT failure returns a failed artifact and never falls back to a grid mesh.
- CLI and Bridge share `NavBakeTileSelection` for dirty/full targets.

Out of scope:

- Movement execution and route selection. Those are owned by MassFlow execution and routing docs.
- Temporary dynamic avoidance. It remains MassFlow runtime avoidance, not navmesh rebuild input.

## User Story

As a level designer, I want the editor and CLI to bake a map with the same parameters, so local preview and CI artifacts match.

Given the same `NavBakeContext`, when the CLI adapter and Bridge adapter call `NavBakeService`, then the tile bytes are identical.

As a runtime engineer, I want structural navmesh changes to reuse the bake service, so runtime rebuilds do not invent a second obstacle or terrain pipeline.

Given a dirty `NavTile` and a `runtime-incremental` context, when the rebuild queue calls `NavBakeService`, then only CDT is allowed and successful tiles are published through the existing query stores.

## UAT Showcase

Current verified commands:

```powershell
dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj --filter "NavBakeServiceContractTests|NavMeshConfigContractTests|NavigationObstacleAuthoringContractTests|NavigationLegacyDomainRemovalContractTests" /m:1 /nr:false --no-restore --logger "console;verbosity=minimal"
```

| Command / operation | Feedback |
|---|---|
| `dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav bake-recast-react --mapId <mapId> --in <map_data.bin> --dirty <dirty.json> --artifact true` | CLI prints `ok=<N> fail=0`; `.ntil` tiles are written under `assets/Data/Nav/<mapId>/layer0/profile_<profile>/...` |
| Bridge `POST /api/nav/bake-recast-react` with the same fields | Response includes `okCount` / `failCount` / tile base64; the same tile matches the CLI output |
| Change `Navigation/navmesh.json` `profiles[].maxClimbCm` and rebake | Tile hash changes and the showcase HUD reports the new tile hash |
| Delete `algorithm` or change casing | Loader fails fast and no fallback tile is produced |
| Run the architecture contract command above | Strict config, no fallback, explicit obstacle/layer, and runtime incremental CDT contracts pass |

## Configuration

`assets/Configs/Navigation/navmesh.json`:

| Field | Value | Owner | Constraint |
|---|---|---|---|
| `mode` | `offline` or `runtime-incremental` | `NavBakeContext.Mode` | Required, strict casing |
| `algorithm` | `recast` or `cdt` | `NavBakeContext.Algorithm` | Required, strict casing; `runtime-incremental` requires `cdt` |
| `profiles[].id` | AgentProfile id | `AgentProfileRegistry` | Must exist, strict casing |
| `profiles[].maxClimbCm` | cm | NavMesh profile | Required number |
| `profiles[].maxSlopeDeg` | degrees | NavMesh profile | Required number |
| `layers[].id` | layer id | Nav layer | Required non-empty trimmed string |
| `layers[].layer` | int | NavTile layer | Required number |
| `areas[]` | area cost list | NavMesh area costs | Required explicit array, may be empty |
| `runtimeIncremental.tileBudgetPerFixedTick` | int | Runtime rebuild queue | Required, `> 0` |
| `runtimeIncremental.includeNeighborTiles` | bool | Dirty AABB aggregation | Required |
| `runtimeIncremental.heightScaleMeters` | float | Runtime `NavBuildConfig` | Required, `> 0` |
| `runtimeIncremental.minWalkableUpDot` | float | Runtime `NavBuildConfig` | Required, `-1..1` |
| `runtimeIncremental.cliffHeightThreshold` | int | Runtime `NavBuildConfig` | Required, `>= 0` |

`sourceUri` must use VFS form such as `Core:Maps/example.vtxm` or `Core:Maps/example.runtime-navmesh`. The service layer records a URI contract and rejects absolute filesystem paths.

Runtime incremental rebuild is enabled only when the top-level config explicitly selects `mode: runtime-incremental` and `algorithm: cdt`. The default offline full-bake config may stay `mode: offline` and `algorithm: recast`; in that mode runtime dirty services are not registered.

## Config To Behavior Tests

| Change | Behavior | Coverage |
|---|---|---|
| `algorithm: recast` | Uses `RecastNavBakeAlgorithm` | `NavBakeServiceContractTests` |
| `algorithm: cdt` | Uses `CdtNavBakeAlgorithm` | `NavBakeServiceContractTests` |
| `mode: runtime-incremental` with non-CDT algorithm | Fails fast | `NavBakeService_RuntimeIncremental_RequiresCdtAlgorithm` |
| Missing `runtimeIncremental` | Loader fails fast | `NavMeshBakeConfig_RequiresExplicitRuntimeIncrementalConfig` |
| CDT triangulation failure | Failed artifact, no grid fallback | `CdtBakePipeline_DoesNotFallbackToGridMesh` |
| Direct CDT pipeline call without obstacles or layer id | Fails fast | `CdtBakePipeline_RequiresExplicitObstacleSetAndLayer` |
| Same context through headless/Bridge adapters | Tile bytes match | `NavBakeService_RunsSingleContextForHeadlessAndBridgeAdapters` |

## Merge And Reuse

Reused:

- `ConfigPipeline` / `ConfigCatalogLoader` for `Navigation/navmesh.json`.
- `AgentProfileRegistry` as the geometry profile SSOT.
- `LogicTerrainField` as the grid/hex terrain input.
- `NavObstacleSet` from the NAV-3 obstacle authoring SSOT.
- `RecastNavTileBaker` and `CdtNavBakeAlgorithm` as `INavBakeAlgorithm` adapters.
- `NavQueryServiceRegistry` and `NavTileStore` for runtime publication.

No private loader, fallback layer injection, or second obstacle data source is allowed.

## DoD

- Data-driven: mode, algorithm, profile, layer, runtime budget, and build tuning all come from config or context.
- No fallback: missing config, bad casing, missing obstacle set, bad layer id, or unsupported algorithm fails fast.
- No duplicate data source: CLI, Bridge, offline bake, and runtime rebuild all go through `NavBakeService` and the NAV-3 obstacle SSOT.
- Strict casing: `offline`, `runtime-incremental`, `recast`, `cdt`, profile ids, and layer ids are all case-sensitive.
- Contract tests cover adapter parity, strict config, no grid fallback, explicit obstacle/layer requirements, and runtime incremental CDT gating.
- GitBook indexes include this page and the runtime incremental follow-up page.
- This page links back to #281, #287, and #304.
