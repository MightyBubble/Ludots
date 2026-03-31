# Visible Checklist: navmesh-bake-terrain-visualization

- `000_map_overview.png` should explain in plain language why this chunk was chosen, not just highlight the center tile.
- `010_chunk_terrain.png` should make the shoreline and height bands visually obvious.
- `020_block_causes.png` should let a reviewer distinguish water, hard obstacles, cliff rejection, and straightened cutouts at a glance.
- `030_walk_mask.png` should show a believable movement footprint instead of a full green rectangle.
- `040_contours.png` should show bright closed loops that match the visible movement footprint.
- `050_polygons.png` should prove polygon cleanup and hole assignment before triangulation.
- `060_trimesh.png` should show a non-trivial final movement surface, with enough triangles to inspect.
- `070_runtime_queries.png` should prove three things: a same-tile path, a portal-crossing path, and blocked-point rejection.
- `080_nav_tile.png` should show the final tile portals lining up with the visible boundary.

- `000_map_overview.png`: Selected showcase chunk inside the terrain benchmark, with auto-pick reason and pass card.
- `010_chunk_terrain.png`: Chosen terrain slice showing shoreline, blocked markup, and height variation.
- `020_block_causes.png`: Per-triangle audit showing why triangles are removed: water, hard-block, cliff, or cliff straightening.
- `030_walk_mask.png`: Final walkable domain after all terrain rules are applied.
- `040_contours.png`: Extracted outer and hole contours outlining valid movement ground.
- `050_polygons.png`: Processed polygons and hole assignment before triangulation.
- `060_trimesh.png`: Final movement surface triangles generated from the processed polygons.
- `070_runtime_queries.png`: Runtime proof: same-tile path, cross-portal traversal, and blocked-point rejection.
- `080_nav_tile.png`: Final nav tile with labeled border portals and triangle coverage.
- `timeline.png`: review all stages side by side.
