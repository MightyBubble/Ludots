# Presenter Blacksmith Showcase Scatter Benchmark

- workload: random-scattered `blacksmith_building` templates on the showcase map
- measured frames: `120` after warmup
- initialization is measured separately until runtime spawn + presenter/presentation counts stop changing
- focus: canonical presenter tree + HUD + spline + decal stability under many blacksmith roots
- note: `tight` scenarios are full-visibility stress; `wide` scenarios validate camera culling / LOD under the same production actor graph

## scatter_25

- seed: `24681357`
- total buildings: `25`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `24`
- initialization: frames `9` | total `31.0632 ms` | max frame `18.2473 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.1768 ms`
- p95 tick: `0.4603 ms`
- max tick: `0.5792 ms`
- avg simulation: `0.0679 ms` | avg presentation: `0.0901 ms`
- avg presenter behavior: `0.0035 ms` | avg animator: `0.0002 ms` | avg emit: `0.0324 ms` | avg request flush: `0.0157 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0325 ms`
- hottest simulation system: `Physics2DSimulationSystem` avg `0.0231 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0047 ms` | p95 culling: `0.0079 ms` | max culling: `0.0340 ms`
- avg HUD projection: `0.0008 ms` | p95 HUD projection: `0.0030 ms` | max HUD projection: `0.0064 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `5656.6`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `50.8939 ms` | max frame `43.3026 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.3275 ms`
- p95 tick: `0.6078 ms`
- max tick: `0.6830 ms`
- avg simulation: `0.0848 ms` | avg presentation: `0.2250 ms`
- avg presenter behavior: `0.0059 ms` | avg animator: `0.0001 ms` | avg emit: `0.1131 ms` | avg request flush: `0.0577 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.1132 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1272 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0037 ms` | p95 culling: `0.0053 ms` | max culling: `0.0134 ms`
- avg HUD projection: `0.0015 ms` | p95 HUD projection: `0.0056 ms` | max HUD projection: `0.0141 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `3053.6`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `539.7528 ms` | max frame `477.8050 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `2.2119 ms`
- p95 tick: `3.6714 ms`
- max tick: `8.4150 ms`
- avg simulation: `0.4520 ms` | avg presentation: `1.7246 ms`
- avg presenter behavior: `0.0430 ms` | avg animator: `0.0002 ms` | avg emit: `0.9461 ms` | avg request flush: `0.5479 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.9463 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.5385 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0053 ms` | p95 culling: `0.0099 ms` | max culling: `0.0157 ms`
- avg HUD projection: `0.0087 ms` | p95 HUD projection: `0.0320 ms` | max HUD projection: `0.0352 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `452.1`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `1259.1081 ms` | max frame `1178.3044 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `4.3291 ms`
- p95 tick: `8.5566 ms`
- max tick: `9.1416 ms`
- avg simulation: `0.6265 ms` | avg presentation: `3.6368 ms`
- avg presenter behavior: `0.0639 ms` | avg animator: `0.0003 ms` | avg emit: `1.2292 ms` | avg request flush: `1.9539 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `1.9496 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.6723 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0113 ms` | p95 culling: `0.0201 ms` | max culling: `0.0711 ms`
- avg HUD projection: `0.0272 ms` | p95 HUD projection: `0.0923 ms` | max HUD projection: `0.1071 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `231.0`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `1299.7330 ms` | max frame `1261.5719 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `4.3258 ms`
- p95 tick: `6.2093 ms`
- max tick: `6.6109 ms`
- avg simulation: `0.5538 ms` | avg presentation: `3.7319 ms`
- avg presenter behavior: `0.0669 ms` | avg animator: `0.0003 ms` | avg emit: `1.2802 ms` | avg request flush: `1.8803 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `1.8805 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.6262 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `27.9`/`105` | attr changes `27.9`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0069 ms` | p95 culling: `0.0088 ms` | max culling: `0.0111 ms`
- avg HUD projection: `0.0189 ms` | p95 HUD projection: `0.0632 ms` | max HUD projection: `0.0794 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `231.2`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `2286.6428 ms` | max frame `2217.6780 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `11.4698 ms`
- p95 tick: `16.7589 ms`
- max tick: `88.1343 ms`
- avg simulation: `1.9315 ms` | avg presentation: `9.4562 ms`
- avg presenter behavior: `0.1989 ms` | avg animator: `0.0003 ms` | avg emit: `3.2394 ms` | avg request flush: `4.5646 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `4.5637 ms`
- hottest simulation system: `PresenterBlacksmithShowcaseKnowledgeProjectionSystem` avg `1.7433 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `62.8`/`347` | attr changes `62.8`/`347` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0103 ms` | p95 culling: `0.0158 ms` | max culling: `0.0246 ms`
- avg HUD projection: `0.0419 ms` | p95 HUD projection: `0.1428 ms` | max HUD projection: `0.2129 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `87.2`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `18081.1647 ms` | max frame `17847.1655 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `46.5728 ms`
- p95 tick: `100.1574 ms`
- max tick: `287.2924 ms`
- avg simulation: `9.8603 ms` | avg presentation: `36.5494 ms`
- avg presenter behavior: `0.7368 ms` | avg animator: `0.0004 ms` | avg emit: `11.2789 ms` | avg request flush: `19.7673 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `19.4329 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `7.0907 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `162.6`/`572` | attr changes `162.6`/`572` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0122 ms` | p95 culling: `0.0204 ms` | max culling: `0.0229 ms`
- avg HUD projection: `0.1122 ms` | p95 HUD projection: `0.3821 ms` | max HUD projection: `1.2022 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `21.5`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `9913.7986 ms` | max frame `9812.2628 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `20.1077 ms`
- p95 tick: `41.3196 ms`
- max tick: `252.9286 ms`
- avg simulation: `8.4672 ms` | avg presentation: `11.5986 ms`
- avg presenter behavior: `0.6202 ms` | avg animator: `0.0005 ms` | avg emit: `6.0701 ms` | avg request flush: `0.8891 ms`
- hottest presentation system: `PresenterEmitSystem` avg `6.0707 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `6.4204 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `166.9`/`572` | attr changes `166.9`/`572` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0098 ms` | p95 culling: `0.0146 ms` | max culling: `0.0194 ms`
- avg HUD projection: `0.0064 ms` | p95 HUD projection: `0.0203 ms` | max HUD projection: `0.0222 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `49.7`

