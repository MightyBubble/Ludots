# Live Map Editor UAT

Epic: [#451](https://github.com/MightyBubble/Ludots/issues/451). This runbook validates the in-session editor path: launch-time capability mod, CEF DataPlane control panel, Raylib authoritative viewport, runtime-incremental CDT rebake, path query, and in-process save.

## Commands

```powershell
dotnet build mods/capabilities/live_map_editor/LiveMapEditorMod/LiveMapEditorMod.csproj
```

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- resolve preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build never --json
```

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- build preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build auto
```

Headed run:

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- launch preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build auto
```

## Manual UAT

| Step | Operation | Expected feedback |
|---|---|---|
| 1 | Launch `live_map_editor_nav_grid_cef_raylib` | Raylib opens `live_editor_nav_grid`, a child of the existing core `nav_editor_grid`; no separate Three.js editor is involved |
| 2 | Press `F4` | CEF overlay panel appears in `UiSurfaceSegment.Overlay` |
| 3 | Inspect the panel | Map id, board list, terrain dimensions, entity count, nav runtime, loaded/pending tiles are visible |
| 4 | Choose Paint, adjust brush, left-drag the Raylib viewport | Core grid `LogicTerrainField` cells change; brush ring and dirty AABB state update |
| 5 | Press Rebake Dirty | Runtime-incremental CDT queue processes dirty tiles; panel shows rebuilt/failed/pending counts |
| 6 | Choose Sim, left-click start and right-click goal in the viewport | `NavQueryService.TryFindPath` runs; Raylib draws start/goal and the query path; panel shows query time in microseconds |
| 7 | Choose Entity, place/select/remove at the picked point | ECS map entity state changes and the DataPlane inspector refreshes |
| 8 | Press Save | `MapAuthoringAssetWriter` writes the authoring map fragment, `.ltrn`, entities, and loaded nav tiles without Bridge HTTP |
| 9 | Relaunch the same preset | Saved terrain/entity/nav authoring changes remain present |

## Configuration

The preset stacks three selectors:

| Selector | Purpose |
|---|---|
| `$browser_cef_runtime` | Provides the CEF browser runtime service used by the overlay panel |
| `$live_map_editor_nav_grid_uat` | Resource-only UAT selector that opens `live_editor_nav_grid`, inheriting the existing core `nav_editor_grid` map and adding `Feature.NavMesh:On` plus an explicit live-editor save target |
| `$live_map_editor` | Capability mod containing the panel, command lane, viewport overlays, and save command |

The editor mod contributes `assets/Configs/Navigation/navmesh.json`:

| Field | Required value for this Epic |
|---|---|
| `mode` | `runtime-incremental` |
| `algorithm` | `cdt` |
| `layers` | Single ground layer for this UAT |
| `runtimeIncremental.tileBudgetPerFixedTick` | Budget for live dirty-tile processing |

Live rebake is not Recast. Recast remains an offline full-bake toolchain option outside the live editor loop.

The UAT grid boards intentionally omit `DataFile`. Core therefore creates flat grid `LogicTerrainField` instances, and the editor saves the first edited grid terrain as `.ltrn`.

## Automated Checks

```powershell
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "LogicTerrainBinaryContractTests|LiveMapEditorLauncherContractTests"
```

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --filter MapAuthoringAssetWriterTests
```

## Save Rules

`MapAuthoringAssetWriter` refuses implicit save targets. It writes to a loaded map fragment with `metadata.liveMapEditor.saveTarget=true`; without that explicit flag it writes only when exactly one loaded mod map fragment for the focused map declares authoring boards. Tag-only overlays are ignored as save targets. Multiple board-declaring fragments without a single explicit target fail fast so the user does not accidentally split the map SSOT.

This editor path does not write an independent `.vhtm` asset. If a map has no explicit visual heightmap, Core derives the Raylib terrain surface from the grid LogicTerrain adapter; that derived visual surface is rebuilt live and is not a second authoring source.
