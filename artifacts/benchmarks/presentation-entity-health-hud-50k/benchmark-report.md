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

- avg total: `72.989 ms`
- p95 total: `86.200 ms`
- max total: `87.459 ms`
- avg HP->HUD sync: `37.851 ms`
- avg HUD->overlay build: `35.137 ms`
- avg fps equivalent: `13.7`
- alloc per frame: `382.7 B`
- avg changed entities: `50000`
- avg dirty lanes: `2.00`
- avg retained overlay items: `0`
- avg mutated overlay items: `100000`
- 60 Hz pass: `no`

## Final Counts

- bars: `50000`
- text: `50000`
- overlay scene items: `100000`
