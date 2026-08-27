# East Asia NavMesh Debug

This data-only overlay enables the offline Recast-baked `Small` Ground layer
and `Medium` Water layer on `east_asia_visual_heightmap`. Recast voxels follow the board's
3571 cm logic cells on a ~64 km playable board (source VHTM samples stay
continental; `VisualHeightmap.WorldWidthCm` remaps world meters). Its 7x4
macro-tile board covers the complete 6399232x3656704 cm playable extent.
`Navigation/agent_profiles.json` keeps `Medium.layer` on Water so ships query
the sea mesh instead of the land mesh.

The overlay also authors a simplified East Asia waterway `TransportNetwork`
(Yangtze corridor, Yellow River corridor, Taihu filled ring) and opts into
`Navigation/transport_nav_obstacle_sink.json` so bake carves those polygons
into the same `NavObstacleSet` as map-authored obstacles. Corridor nodes are
Albers-projected lon/lat samples scaled with the playable board; carve width
comes from `widthCm`; presentation ribbon width stays on `visualWidthMeters`.

It does **not** depend on MapField / country ownership. National borders live in
`FieldEastAsiaCountryMod` and stay on that showcase. Press `N` / `T` here for the
baked nav triangles and walkability texture.

Bake the authoritative continuous VisualHeightmap from the repository root:

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav bake-vhtm `
  --mapId east_asia_visual_heightmap `
  --modId EastAsiaNavMeshDebugMod `
  --in mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/samples/LudotsSample/east_asia/east_asia_continuous.vhtm `
  --outDir . `
  --seaLevelCm 0 `
  --heightStep 100 `
  --heightScale 1 `
  --parallel true `
  --artifact false `
  --large-bake true `
  --estimateHash <hash printed by the first run>
```

Bake the inverted water mesh (`Medium` × `Water` only) after the land tiles exist:

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav bake-vhtm `
  --mapId east_asia_visual_heightmap `
  --modId EastAsiaNavMeshDebugMod `
  --in mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/samples/LudotsSample/east_asia/east_asia_continuous.vhtm `
  --outDir . `
  --seaLevelCm 0 `
  --waterWalkable true `
  --profiles Medium `
  --layers Water `
  --heightStep 100 `
  --heightScale 1 `
  --parallel true `
  --artifact false `
  --large-bake true `
  --estimateHash <hash printed by the first run>
```

Export the world-aligned walkability texture:

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav export-walkability-texture `
  --mapId east_asia_visual_heightmap `
  --modId EastAsiaNavMeshDebugMod `
  --profile Small `
  --repoRoot . `
  --out mods/showcases/east_asia_navmesh_debug/EastAsiaNavMeshDebugMod/assets/Textures/nav_walkability.png `
  --width 4096 `
  --minXcm -3199616 `
  --minZcm -1828352 `
  --maxXcm 3199616 `
  --maxZcm 1828352 `
  --vhtm mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/samples/LudotsSample/east_asia/east_asia_continuous.vhtm `
  --seaLevelCm 0
```

Launch with:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:east_asia_navmesh_debug_raylib'
```

`N` toggles baked NavMesh triangles and `T` toggles the projected walkability texture
（由 Raylib preset 显式挂载 `NavMeshDebugLaunchMod`；本 Mod 只提供数据与棋盘叠加，不硬依赖调试输入）。
