# Presenter Blacksmith Showcase Scatter Benchmark

- workload: random-scattered `blacksmith_building` templates on the showcase map
- measured frames: `120` after warmup
- initialization is measured separately until runtime spawn + presenter/presentation counts stop changing
- focus: canonical presenter tree + HUD + spline + decal stability under many blacksmith roots
- note: `tight` scenarios are full-visibility stress; `wide` scenarios validate camera culling / LOD under the same production actor graph

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `12745.9733 ms` | max frame `12666.7860 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `18.1316 ms`
- p95 tick: `61.0908 ms`
- max tick: `349.9950 ms`
- avg simulation: `11.2637 ms` | avg presentation: `6.6155 ms`
- avg presenter behavior: `0.6960 ms` | avg animator: `0.0007 ms` | avg emit: `1.8099 ms` | avg request flush: `0.0019 ms`
- hottest presentation system: `InstancedBatchEmissionSystem` avg `3.2969 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `9.0057 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `162.6`/`572` | attr changes `162.6`/`572` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0154 ms` | p95 culling: `0.0293 ms` | max culling: `0.0551 ms`
- avg HUD projection: `0.1935 ms` | p95 HUD projection: `0.6964 ms` | max HUD projection: `1.2223 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `55.2`

