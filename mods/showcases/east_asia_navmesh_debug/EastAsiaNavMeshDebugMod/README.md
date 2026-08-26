# East Asia NavMesh Debug

This data-only overlay enables the offline Recast-baked `Small` navigation
profile on `east_asia_visual_heightmap`. Recast voxels follow the board's
502596 cm logic cells (strategy resolution), not the tactical 5–50 cm agent
clamp. Its 7x4 macro-tile board covers the complete 900652032x514658304 cm
VisualHeightmap.

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

Export the world-aligned walkability texture:

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav export-walkability-texture `
  --mapId east_asia_visual_heightmap `
  --modId EastAsiaNavMeshDebugMod `
  --profile Small `
  --repoRoot . `
  --out mods/showcases/east_asia_navmesh_debug/EastAsiaNavMeshDebugMod/assets/Textures/nav_walkability.png `
  --width 2048 `
  --minXcm -450326016 `
  --minZcm -257329152 `
  --maxXcm 450326016 `
  --maxZcm 257329152
```

Launch with:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:east_asia_navmesh_debug_raylib'
```

`N` toggles baked NavMesh triangles and `T` toggles the projected walkability texture.
