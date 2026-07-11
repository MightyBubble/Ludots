# MassNavigation Issue #642 10K Performance Comparison

Date: 2026-07-11
Scenario: `$capability_standard_mass_navigation_large_world_10k`
Measurement: 300 warmup ticks, then 60 wall-clock seconds with MassNavigation timing and presentation system breakdown disabled; 128 command actors alternate between two targets every 5 seconds through `OrderBuffer`.

All heap/allocation/working-set values are process-wide headless launcher measurements. They are not MassNavigation-only attribution.

| Stage | Prepared agents | Managed heap start | Working set start | Ticks/s | Avg tick | Max tick | Alloc/s | Storage growth | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Before capacity remediation | 160000 | 1849752320 B | 2321465344 B | 34.943 | 27.192 ms | 303.016 ms | 82830 B/s | 0 | Functional checks passed except obsolete transient-group assertion |
| Agent capacity fixed, old presentation blanket capacities | 10000 | 1821791880 B | 2321694720 B | 43.675 | 21.966 ms | 55.245 ms | 102364 B/s | 0 | PASS |
| Right-sized capacities, pre-final instrumentation | 10000 | 1141283680 B | 1897574400 B | 43.762 | 21.918 ms | 46.810 ms | 102583 B/s | 0 | Rejected as final evidence: later regression exposed incomplete screen HUD capacity |
| Final-stable confirmation A | 10000 | 1153977696 B | 1889509376 B | 32.021 | 28.580 ms | 264.625 ms | 76242 B/s | 0 | PASS; shared-machine performance variance observed |
| Final-stable isolated confirmation | 10000 | 1153128760 B | 1889894400 B | 38.585 | 24.282 ms | 297.353 ms | 90835 B/s | 0 | PASS; build servers stopped and no workspace test/build/launcher process remained |

## Measured Change

- Managed heap start: `-696623560` bytes (`-37.66%`) from the before-remediation run to the isolated final confirmation.
- Working set start: `-431570944` bytes (`-18.59%`).
- Average headless tick: `-2.910ms` (`-10.70%`).
- Headless tick throughput: `+3.642 ticks/s` (`+10.42%`).
- Initial order proof: `128` submitted actors, `128` moved actors.
- HUD completeness proof: `20000` world HUD entries, `10000` screen bars, `10000` screen texts, and `0` dropped screen-HUD entries after the order.
- Final overlap proof: `0` deep-overlap pairs; final penetration ratio `3.98%`.
- Final isolated steady-state proof: `60.050s`, `2317` ticks, `12` workload orders, `0` agent-storage growth.

## Final-Run Variance

The two post-review final-stable confirmations both passed the same functional, HUD, memory, and capacity checks. Their measured throughput ranged from `32.021` to `38.585` ticks/s and average tick time ranged from `28.580ms` to `24.282ms`. The isolated confirmation was run after `dotnet build-server shutdown`, with no workspace test, build, or launcher process remaining; startup CPU samples were still `36.9%`, `43.9%`, and `22.5%`, so the report preserves both runs instead of selecting the historical best result.

## Evidence

- Canonical final: `artifacts/acceptance/mass-navigation-issue-642/summary.json`
- Readable report: `artifacts/acceptance/mass-navigation-issue-642/battle-report.md`
- Machine trace: `artifacts/acceptance/mass-navigation-issue-642/trace.jsonl`
- Test path: `artifacts/acceptance/mass-navigation-issue-642/path.mmd`
- Before run: `artifacts/acceptance/mass-navigation-issue-642/runs/20260711-052340/run-0001/summary.json`
- Agent-capacity-only run: `artifacts/acceptance/mass-navigation-issue-642/runs/20260711-053543/run-0001/summary.json`
- Pre-final right-sized run: `artifacts/acceptance/mass-navigation-issue-642/runs/20260711-054235/run-0001/summary.json`
- Final-stable confirmation A: `artifacts/acceptance/mass-navigation-issue-642/runs/20260711-080903/run-0001/summary.json`
- Final-stable isolated confirmation: `artifacts/acceptance/mass-navigation-issue-642/runs/20260711-081353/run-0001/summary.json`

## Residual Risk

The isolated final process still retains about `1.153GB` managed heap and `1.890GB` working set for 10,000 agents plus 30,009 active performers and the full headless evidence host. The run proves a large reduction and stable steady state, not subsystem-exclusive memory ownership. Further reduction requires measured performer/ECS attribution rather than another blanket capacity cut.
