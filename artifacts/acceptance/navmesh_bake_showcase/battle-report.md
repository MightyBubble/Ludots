# Scenario Card: navmesh-bake-terrain-visualization

## Plain-Language Pass Card
- Pass if land stays walkable, water and cliffs are visibly cut out, the outline matches the final mesh, border openings become portals, and the runtime query crosses a portal while rejecting a blocked sample.

## Intent
- Player goal: verify that movement space matches the terrain in an obvious way, instead of trusting a black-box navmesh export.
- Engine-user goal: inspect the same shoreline chunk across terrain, cause audit, contours, polygon cleanup, triangulation, portals, and runtime path queries.

## Determinism Inputs
- Adapter: `raylib`
- Root mod: `TerrainBenchmarkMod`
- Input map: `D:\001_AI\LudotsDev\Ludots-issue78-acceptance\mods\TerrainBenchmarkMod\assets\Data\Maps\terrain_bench.vtxm`
- Chunk: `30,27`
- Auto-pick reason: Auto-picked chunk 30,27: water=28%, blockedVertices=0%, shorelineTransitions=97, polygons=1, holes=0, triangles=38, portals=2.
- Build config: heightScale=`2.0`, minWalkableUpDot=`0.6`, cliffThreshold=`1`

## Action Script
1. Load the generated terrain benchmark vertex map from the clean worktree.
2. Auto-select the most legible shoreline chunk instead of hard-pinning the center tile.
3. Export terrain, blocked-cause audit, final walk mask, contours, polygons, triangulated mesh, runtime query proof, and final portalized tile.
4. Fail if any structural bake stage collapses, if blocked reasons are not visible, or if runtime queries do not prove the baked result is usable.

## Outcome
- success: yes
- verdict: NavMesh bake acceptance selected shoreline chunk (30,27) with visible cutouts, 38 triangles, and 2 portals.
- walkable triangles: `5704`
- blocked by water: `2487`
- blocked by hard obstacle: `0`
- blocked by cliff: `0`
- blocked by straightening: `1`
- contour rings: `1`
- polygons: `1`
- polygon holes: `0`
- mesh vertices: `40`
- mesh triangles: `38`
- border portals: `2`
- used grid fallback: `False`
- runtime same-tile path: `True` with `2` points
- runtime cross-portal path: `True` with `4` points
- blocked sample rejected: `True`
- normalized signature: `navmesh-bake-terrain-visualization|chunk:30,27|walk:5704|water:2487|cliff:0|rings:1|polys:1|holes:0|verts:40|tris:38|portals:2|fallback:0`

## Visual Evidence
- `screens/000_map_overview.png`: Selected showcase chunk inside the terrain benchmark, with auto-pick reason and pass card.
- `screens/010_chunk_terrain.png`: Chosen terrain slice showing shoreline, blocked markup, and height variation.
- `screens/020_block_causes.png`: Per-triangle audit showing why triangles are removed: water, hard-block, cliff, or cliff straightening.
- `screens/030_walk_mask.png`: Final walkable domain after all terrain rules are applied.
- `screens/040_contours.png`: Extracted outer and hole contours outlining valid movement ground.
- `screens/050_polygons.png`: Processed polygons and hole assignment before triangulation.
- `screens/060_trimesh.png`: Final movement surface triangles generated from the processed polygons.
- `screens/070_runtime_queries.png`: Runtime proof: same-tile path, cross-portal traversal, and blocked-point rejection.
- `screens/080_nav_tile.png`: Final nav tile with labeled border portals and triangle coverage.
- `screens/timeline.png`: compact stage gallery for review.
