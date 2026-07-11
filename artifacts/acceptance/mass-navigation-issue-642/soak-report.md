# MassNavigation Large-World UAT Soak

## Scope
- Target: `MassNavigationMod`, the high-performance MassNavigation foundation acceptance surface.
- Launcher path: `scripts/run-mod-launcher.cmd cli launch '$capability_standard_mass_navigation_large_world_10k' --adapter raylib --record ...`.
- Evidence per run: `battle-report.md`, `trace.jsonl`, `path.mmd`, `summary.json`, `visible-checklist.md`, and `screens/timeline.png`.
- Canonical latest successful run: `artifacts/acceptance/mass-navigation-issue-642/{battle-report.md,trace.jsonl,path.mmd,summary.json}`.
- Performance measurement: timing-disabled, process-wide allocation/GC/working-set evidence plus solver agent-storage allocation-count delta.
- Measurement scope is the full headless launcher process; allocation and working-set values are not presented as MassNavigation-only attribution.

## Result
- Output root: `D:\003_Ludots\Ludots_issue642_massnav\artifacts\acceptance\mass-navigation-issue-642`
- Session runs: `D:\003_Ludots\Ludots_issue642_massnav\artifacts\acceptance\mass-navigation-issue-642\runs\20260711-081353`
- Deadline: ``
- Runs: `1`
- Passed: `1`
- Failed: `0`

## UAT Matrix
| Case | Player-facing expectation | Machine check |
| --- | --- | --- |
| 64km world boot | Designer sees one standard RTS battlefield | `world_width_cm == 6400000 && world_height_cm == 6400000` |
| Four dynamic teams | Scenario is not a hard-coded two-team demo | `teams >= 4` |
| Full minimap | Minimap starts as the whole world | full-world half extent check |
| Camera jumps | Clicking minimap coordinates moves camera exactly there | 12 target tolerances, including all corners and empty space |
| 10K binding | Configured crowd binds through production ECS/runtime path | `agent_count == 10000` and ECS count matches |
| Formal command source/order | Selected command-source agents enter OrderBuffer and move even if the short-lived group completes before the snapshot | non-zero submitted orders and moved command actors |
| Complete health HUD | All 10K bars and 10K texts survive world-to-screen projection | exact bar/text counts and zero screen-HUD drops |
| Camera/minimap residency | Remote minimap jump does not respawn/reset the scenario | stable agent/spawn/reset counts |
| Avoidance | Central crowd overlap resolves through the production solver | final overlap/penetration checks |
| Timing-disabled steady state | Instrumentation does not dominate the measured interval | `steady_timing_disabled == true` |
| Capacity stability | Solver agent storage is prepared before the interval and does not grow | `steady_capacity_growth_events == 0` |
| Memory evidence | Process-wide GC, retained heap and working set are reported without subsystem attribution | exact `steady_*` byte/count fields |

## Runs
| # | Result | Signature | Duration s | Ticks/orders | Avg tick ms | Alloc MB/s | Retained MB | WS growth MB | Peak WS MB | Capacity growth | Timeline |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | PASS | `mass_navigation_large_world|agents:10000|teams:4|performers:30009|markers:10009/0|orders:128/moved:128|remote:2400000,2100000|spawns:2->2|resets:1->1|avoidance:181/2500/339/0/0.0398|steady:60s/capacity-growth:0` | 60.050 | 2317/12 | 24.282 | 0.09 | -0.20 | 5.08 | 1808.83 | 0 | `D:\003_Ludots\Ludots_issue642_massnav\artifacts\acceptance\mass-navigation-issue-642\runs\20260711-081353\run-0001\screens\timeline.png` |

## Failure Handling
- If any run fails, inspect that run directory first; it keeps stdout/stderr in `run.log` and the last evidence screenshots.
- Do not treat a missing service or missing summary as a fallback success. Missing evidence is a failed run.
- Headless evidence does not claim live render FPS. Use the Raylib HUD or a renderer benchmark for real FPS.
