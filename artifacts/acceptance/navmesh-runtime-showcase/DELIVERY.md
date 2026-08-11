# NavMesh Runtime Showcase — Delivery Report

## Delivered Components

### 1. NavMesh Visualization (Phase 1)
- **Raylib renderer integration**: `src/Client/Ludots.Client.Raylib/RaylibPresentationWorldRenderer.cs:603-646`
  - Semi-transparent navmesh fill rendering
  - Edge overlay rendering with configurable height offset
- **Presentation state**: `NavMeshPresentationState` configured in showcase mod entry
- **Capability**: LayeredSpan algorithm with runtime-incremental mode

### 2. Runtime Obstacle System (Core)
- **Dirty queue**: `RuntimeNavMeshObstacleDirtySystem` auto-registered at `GameEngine.cs:1839`
- **Component**: `RuntimeNavMeshObstacle` marks entities for rebake
- **Integration**: Pumps dirty entities to `RuntimeIncrementalNavMeshRebuildQueue` each tick

### 3. NavMeshRuntimeShowcaseMod
**Location**: `mods/showcases/navmesh/NavMeshRuntimeShowcaseMod/`

**Structure**:
- `NavMeshRuntimeShowcaseMod.csproj` — builds successfully
- `mod.json` — registered as `navmesh_runtime_showcase` preset
- `NavMeshRuntimeShowcaseModEntry.cs` — enables navmesh visualization (fill: RGBA 0.16,0.75,1.0,0.35; edge: 0.08,0.35,0.63,0.92)
- `NavMeshShowcaseObstacleCycleSystem.cs` — spawns/despawns 100cm square obstacle at (200,200) every 3 seconds
- `assets/game.json` — window title, 50 FPS, navmesh tile capacity 512
- `assets/Maps/navmesh_runtime_demo.json` — GridBoard 2×2 macrotiles, Feature.NavMesh:On tag
- `assets/Configs/Navigation/navmesh.json` — LayeredSpan runtime-incremental config with 3×3 initial bake window

**Capabilities Confirmed**:
- Mod compiles (0 errors, Release build)
- Launcher successfully loads mod stack: LudotsCoreMod → CoreInputMod → NavMeshRuntimeShowcaseMod
- Map loads with LogicTerrain 512×512 cells
- NavMesh config merges from both Core and mod fragments
- MapLoaded event fires with no exceptions

## Visual Evidence

**Screenshot**: `artifacts/acceptance/navmesh-runtime-showcase/screenshot-running.png` (707 KB)

**Pixel analysis** (sampled every 2px across 1920×1080 capture):
- **98,605 pixels** of bucket (24,168,240) — strong match for configured fill color (41,191,255)
- **172,993 total blue/cyan pixels** matching navmesh heuristic (b>150, g>110, b>r+60)
- Top non-black color is the navmesh blue, appearing 98,605 times vs 22,361 for UI white

This pixel signature is consistent with a semi-transparent blue navmesh overlay blended over a darker ground plane.

## Blockers Resolved

1. **Arch netstandard2.1 build error** — `ArgumentNullException.ThrowIfNull` unavailable in netstandard2.1. Fixed by reverting to manual null check (matching prior fix at commit `b968fc6a`).

2. **NavMesh config mismatches**:
   - Initial `algorithm: "recast"` → Recast is external/offline-only. Changed to `"layered-span"` (the only built-in runtime adapter).
   - Missing agent profiles → Added Small/Medium/Large to match PathingConfig defaults.
   - Invalid properties → Removed invented `detourNavmeshHeaderBytes`/`detourTileMeshBytes`, corrected `triangleSurface.tileResolutionMeters` to `haloPaddingCm`.

3. **Map bootstrap requirements**:
   - Added `Feature.NavMesh:On` tag to trigger navmesh loading.
   - Added GridBoard config to create LogicTerrainField (required by navmesh bootstrap).
   - Added `LoadedChunkCapacity` to satisfy GridBoard validation.

4. **Color range error** — NavMesh presentation colors must be [0,1]. Fixed RGBA from 0-255 scale to normalized floats.

5. **LocalPlayerId mismatch** — Removed `startupLocalPlayerId: 1` (no player entity declared; default 0 is correct for showcases).

## Remaining Work

**M5 milestone blocked**: `VisualRuntimeEmitSystem` + `VisualRuntimeState` (second render path bypassing performer, producer `ChunkSurfaceBakeSystem`) is HIGH priority but excluded from this batch per follow-up plan.

**Manual verification needed**: 
- Confirm navmesh rebake cycle triggers every 3 seconds (obstacle spawn/despawn)
- Verify hole appears in navmesh when obstacle is present
- Capture frame sequence showing navmesh → hole → navmesh restoration

**No recording scenario registered**: Launcher evidence recorder has no `EvidenceScenario` enum entry for this showcase, so `--record` flag is unsupported. Visual capture requires manual interaction or extending `LauncherEvidenceRecorder.cs`.

## Acceptance Status

- ✅ Mod compiles and loads
- ✅ NavMesh config valid and merged
- ✅ Map loads with LogicTerrain + navmesh bootstrap
- ✅ No runtime exceptions
- ✅ Visual evidence shows 98k+ navmesh-blue pixels
- ⚠️ Dynamic rebake behavior unverified (manual interaction required)
- ⚠️ No battle-report/trace artifacts (evidence recorder not wired)

**Verdict**: Core infrastructure delivered and running. Visual proof via pixel analysis confirms navmesh overlay. Dynamic behavior requires hands-on verification or extended recording contract.
