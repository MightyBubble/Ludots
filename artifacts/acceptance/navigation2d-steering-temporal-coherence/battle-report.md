# Scenario Card: navigation2d-steering-temporal-coherence

## Intent
- Player goal: keep dense 2D crowds responsive without changing steering output.
- Gameplay domain: local avoidance / steering hot path.

## Determinism Inputs
- Seed: none
- Map: synthetic static opposing crowd, no flow field.
- Clock profile: fixed `1/60s`, `6` steering ticks.
- Initial entities: `32` nav agents with point goals and explicit kinematics.
- Config source: `Navigation2D.Steering.TemporalCoherence` through `Navigation2DConfig.CloneValidated()`.

## Action Script
1. Run the same dense crowd once with temporal coherence disabled.
2. Run it again with temporal coherence explicitly enabled.
3. Record per-tick cache lookups, hits, and stores.
4. Compare final desired velocities against the cache-disabled reference.

## Expected Outcomes
- Primary success condition: cache-enabled run records cache hits in steady-state ticks.
- Failure branch condition: cache changes desired steering output or never reuses results.
- Key metrics: cache lookups, hits, stores, hit rate, max desired-velocity delta vs reference.

## Evidence Artifacts
- `artifacts/acceptance/navigation2d-steering-temporal-coherence/trace.jsonl`
- `artifacts/acceptance/navigation2d-steering-temporal-coherence/battle-report.md`
- `artifacts/acceptance/navigation2d-steering-temporal-coherence/path.mmd`

## Timeline
- [T+001] Tick#1 | Agents=32 | CacheLookups=32 | CacheHits=0 | CacheStores=32 | HitRate=0.0%
- [T+002] Tick#2 | Agents=32 | CacheLookups=32 | CacheHits=32 | CacheStores=0 | HitRate=100.0%
- [T+003] Tick#3 | Agents=32 | CacheLookups=32 | CacheHits=32 | CacheStores=0 | HitRate=100.0%
- [T+004] Tick#4 | Agents=32 | CacheLookups=32 | CacheHits=32 | CacheStores=0 | HitRate=100.0%
- [T+005] Tick#5 | Agents=32 | CacheLookups=32 | CacheHits=32 | CacheStores=0 | HitRate=100.0%
- [T+006] Tick#6 | Agents=32 | CacheLookups=32 | CacheHits=32 | CacheStores=0 | HitRate=100.0%

## Outcome
- success: yes
- verdict: explicit temporal coherence reuses steady-state steering solves while preserving final desired velocities.
- reason: total cache hit rate reached `83.3%` with max desired delta `0.0000` cm/s vs the cache-disabled reference.

## Summary Stats
- agent count: `32`
- total cache lookups: `192`
- total cache hits: `160`
- total cache stores: `32`
- cache hit rate: `83.3%`
- max desired delta vs reference: `0.0000` cm/s
- reusable wiring: config via `Navigation2D.Steering.TemporalCoherence`, runtime via `Navigation2DWorld`, HUD via `ScreenOverlayBuffer`
