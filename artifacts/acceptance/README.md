# Issue 78 Movement Acceptance Package

## Landing Path

After reviewing issues `#70`-`#78` and `PR #73`, this branch lands the movement path in the order implied by issue `#78`:

1. Keep the runtime path clean by standing on the already landed blockers and governance work from `#74` and `#76`.
2. Reuse the loaded-graph runtime path from `#70` instead of inventing a second movement/streaming stack.
3. Explicitly avoid the bad runtime contract from `PR #73` / `#75`.
4. Ship acceptance mods, evidence recording, screenshots, and videos on top of that clean runtime path.

## PR 73 Guardrails

The accepted path does **not** do either of these:

- mutate authored move-order payloads at runtime
- read live cursor/selection state back out of authored move-order payloads

That keeps movement intent, authoritative input, and runtime state separated.

## Showcase Matrix

- `road_network_showcase`
  - Focus: road routing, route preview, command acceptance, cue visibility, and loaded-chunk migration during long road travel.
  - Evidence: `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `road_network_showcase_command_and_chunking|selected:Blue Vanguard|controlled:Blue Vanguard|command:0,0|status:Grand Road selected Direct corridor with 17 sampled point(s).|blue:-9800->-7336|...|roads:28|cue:1`

- `chunk_streaming_showcase`
  - Focus: camera-window driven chunk loading, east gate push, red-capital push, and reset-to-center unload/reload proof.
  - Evidence: `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `chunk_streaming_showcase_camera_windows|start:...|east:...|red:...|reset:...|splines:11->6`

- `navigation2d/pass_through_collision`
  - Focus: two-team pass-through with symmetric collision avoidance and no dead center lock at the end state.
  - Evidence: `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `navigation2d-pass-through-collision|mid:7516/7380|final:16475/16263|center:0/128|stopped:0|peak:82@450`

- `navigation2d/lane_merge_hybrid`
  - Focus: hybrid lane-merge pressure, merge throughput, and final center clearance.
  - Evidence: `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `navigation2d-lane-merge-hybrid|mid:5239/5562|final:13277/13553|center:0/128|stopped:0|peak:61@600`

- `navigation2d/bottleneck_obstacle`
  - Focus: obstacle constriction throughput under crowd pressure, with scenario-specific acceptance tuned to crossing instead of full center clearance.
  - Evidence: `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `navigation2d-bottleneck-obstacle|mid:5764/5786|final:9716/9449|center:93/128|stopped:35|peak:98@600`

- `navmesh_bake_showcase`
  - Focus: terrain benchmark navmesh bake visualization from map overview -> terrain slice -> blocked-cause audit -> walk mask -> contours -> polygons -> trimesh -> runtime proof -> final nav tile.
  - Evidence: `artifact-report.txt`, `battle-report.md`, `summary.json`, `screens/timeline.png`, `demo.mp4`
  - Signature: `navmesh-bake-terrain-visualization|chunk:30,27|walk:5704|water:2487|cliff:0|rings:1|polys:1|holes:0|verts:40|tris:38|portals:2|fallback:0`

## Important Fixes Behind The Package

- `TerrainBenchmarkMapGenerator` now validates the existing `terrain_bench.vtxm` header and regenerates stale benchmark assets instead of silently reusing an old file.
- The navmesh recorder now reuses the same terrain benchmark generator, so the evidence path and mod path share one benchmark source.
- The navmesh recorder also writes reports and images against the actual selected chunk instead of hardcoded labels.
- `chunk_streaming_showcase` was rerun so it now includes the full screenshot set, visible checklist, and video evidence.

## Evidence Entry Points

- `road_network_showcase/demo.mp4`
- `chunk_streaming_showcase/demo.mp4`
- `navigation2d/pass_through_collision/demo.mp4`
- `navigation2d/lane_merge_hybrid/demo.mp4`
- `navigation2d/bottleneck_obstacle/demo.mp4`
- `navmesh_bake_showcase/demo.mp4`

## Acceptance Notes

- The terrain benchmark asset in this clean worktree was rebuilt to the intended `64x64` chunk footprint before navmesh recording.
- The navmesh bake proof is intentionally visual and stage-based, so the acceptance reviewer can inspect where terrain, water, blocked-cause audit, contouring, triangulation, portals, and runtime traversal come from.
