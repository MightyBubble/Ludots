# Presentation Entity Health HUD 50k Benchmark

- workload: `50000` Arch ECS entities with `AttributeBuffer.Health`
- HUD output: `50000` bars + `50000` text items
- health churn: every measured frame mutates every entity to a deterministic random HP value
- measured frames: `30` after `4` warmup frames
- target frame budget: `16.67 ms` at 60 Hz

## Correctness

- validated entities: `50000`
- bar/text mismatches: `0`
- final HP checksum: `25034917`
- final text checksum: `25034917`
- screen HUD drops: `0`
- overlay scene drops: `0`

## Throughput

- avg total: `23.534 ms`
- p95 total: `24.794 ms`
- max total: `24.814 ms`
- avg HP->HUD sync: `15.466 ms`
- avg HUD->overlay build: `8.068 ms`
- avg fps equivalent: `42.5`
- alloc per frame: `382.7 B`
- avg changed entities: `50000`
- avg dirty lanes: `2.00`
- avg retained overlay items: `0`
- avg mutated overlay items: `0`
- 60 Hz pass: `no`

## Final Counts

- bars: `50000`
- text: `50000`
- overlay scene items: `100000`
