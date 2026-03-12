# Tech Debt Report: nav-playground-steering-60hz-gap

Date: 2026-03-12
Reporter: Codex
Owner: Navigation2D / Physics2D
Severity: P1
Scope: Cross-layer

## Trigger
- Scenario: `Navigation2DPlaygroundMod` is formally playable and avoids collisions, but real crowd movement still stalls and spikes badly enough to feel unshippable.
- Entry point: `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib`
- Repro steps:
  1. Launch the real `nav_playground` scene through the unified launcher.
  2. Keep the default `Pass Through` setup at `64` agents per team.
  3. Box-select one side and issue a cross-lane move order.
  4. Observe visible stutter during congestion even though agents do avoid each other.

## Evidence
- Playable acceptance still shows large runtime spikes on real command ticks:
  - `artifacts/acceptance/navigation2d-playground-playable/battle-report.md`
  - `move_selected` tick: `185.239ms`
  - `command_mode` tick: `181.419ms`
  - max headless tick: `416.060ms`
- Steering benchmark evidence points at avoidance hot path, not flow:
  - `src/Tests/Navigation2DTests/Navigation2DBenchmarkTests.cs`
  - latest local benchmark summary:
    - `StaticCrowd 10k`: `1.32ms - 1.46ms`
    - `OscillatingInCell 10k`: `4.71ms - 5.00ms`
    - `QuarterCrossCellMigration 10k`: `4.18ms - 4.58ms`
- Flow field cost is negligible relative to steering:
  - `src/Core/Navigation2D/FlowField/CrowdFlow2D.cs`
  - `src/Tests/Navigation2DTests/Navigation2DFlowStreamingTests.cs`
  - latest local flow benchmark summary: `0.0044ms - 0.0045ms`
- Spatial update is not the primary bottleneck in the measured 10k migration case:
  - `src/Core/Navigation2D/Runtime/Navigation2DWorld.cs`
  - `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
  - `QuarterCrossCellMigration` cell-map update was measured at about `0.086ms - 0.098ms`
- Hot-path GC evidence is clean, so the current issue is compute reuse and solver pressure:
  - `src/Tests/Navigation2DTests/Navigation2DSteeringSoATests.cs`
  - benchmark samples recorded `AllocatedBytes(CurrentThread) = 0`
- Default product config still ships temporal coherence disabled:
  - `assets/Configs/Navigation2D/navigation2d.json`

## Impact
- User-visible impact: the mod is playable for correctness validation, but crowd motion still feels heavily stuttered in real interaction.
- Correctness/stability risk: teams may incorrectly conclude that obstacle avoidance is "done" because pathing is functionally correct while frame stability is not.
- Blast radius: affects the formal playground demo, launcher-based acceptance confidence, and any future gameplay feature that depends on dense crowd steering.

## Fuse Decision
- Mode: explicit-degrade
- Reason: keep the formal playable pipeline and acceptance flow available, but cap the default playground to a low-density validation baseline (`64/team`, spawn batch `16`) and treat 10k/60Hz as explicitly not met.
- Observability fields:
  - playable acceptance tick timings in `navigation2d-playground-playable`
  - steering cache lookups / hits in `Navigation2DBenchmarkTests`
  - cell-map dirty-agent and migration instrumentation

## Containment and Follow-up
- Immediate containment:
  - keep the default demo density low enough to validate control flow, not final-scale performance
  - keep benchmark and acceptance evidence separated so 10k stress does not masquerade as a product-ready scene
- Status update:
  - landed steering-cell auto sizing so local-avoidance buckets are no longer pinned to the world-grid cell size:
    - `src/Core/Navigation2D/Spatial/Nav2DCellMap.cs`
    - `src/Core/Navigation2D/Runtime/Navigation2DRuntime.cs`
    - `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
    - `src/Tests/Navigation2DTests/Navigation2DSteeringSoATests.cs`
  - this narrows one concrete gap against `projectdawn`: neighbor queries now scale to agent neighbor distance instead of a fixed map cell.
  - this does not close the main remaining gaps:
    - neighbor gather is still per-agent ring traversal instead of partition-once bucket-window traversal
    - steering still writes physics-facing outputs directly instead of a fully decoupled desired-velocity-only contract
- Permanent fix direction:
  - add a formal 10k launcher-driven acceptance path instead of relying only on synthetic benchmark harnesses
  - turn steering cache / temporal coherence into a product-configured path with measured hit-rate targets, not a dormant config branch
  - reduce per-agent recomputation under dynamic crowds by introducing reusable neighbor-set / dirty-region steering invalidation instead of full eager solve pressure
  - continue obstacle and lane-bias modeling through shared steering inputs instead of mod-local hacks
- Target milestone: next Navigation2D performance hardening window before any claim of `10k @ 60Hz` readiness
