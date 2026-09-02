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
- initialization: frames `9` | total `15.5738 ms` | max frame `10.7422 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.1603 ms`
- p95 tick: `0.3038 ms`
- max tick: `4.6176 ms`
- avg simulation: `0.0829 ms` | avg presentation: `0.0644 ms`
- avg presenter behavior: `0.0028 ms` | avg animator: `0.0001 ms` | avg emit: `0.0249 ms` | avg request flush: `0.0129 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0250 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.0481 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0033 ms` | p95 culling: `0.0077 ms` | max culling: `0.0165 ms`
- avg HUD projection: `0.0007 ms` | p95 HUD projection: `0.0044 ms` | max HUD projection: `0.0091 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `6239.5`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `46.3547 ms` | max frame `37.7127 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.4402 ms`
- p95 tick: `0.9405 ms`
- max tick: `3.3988 ms`
- avg simulation: `0.1503 ms` | avg presentation: `0.2668 ms`
- avg presenter behavior: `0.0092 ms` | avg animator: `0.0002 ms` | avg emit: `0.1224 ms` | avg request flush: `0.0722 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.1223 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1813 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0060 ms` | p95 culling: `0.0113 ms` | max culling: `0.0158 ms`
- avg HUD projection: `0.0018 ms` | p95 HUD projection: `0.0074 ms` | max HUD projection: `0.0096 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `2271.5`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `457.4554 ms` | max frame `362.7288 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `4.5594 ms`
- p95 tick: `9.3382 ms`
- max tick: `23.9702 ms`
- avg simulation: `1.9777 ms` | avg presentation: `2.5072 ms`
- avg presenter behavior: `0.0605 ms` | avg animator: `0.0005 ms` | avg emit: `1.1733 ms` | avg request flush: `0.8131 ms`
- hottest presentation system: `PresenterEmitSystem` avg `1.1738 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `4.4458 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0208 ms` | p95 culling: `0.0381 ms` | max culling: `0.0441 ms`
- avg HUD projection: `0.0124 ms` | p95 HUD projection: `0.0411 ms` | max HUD projection: `0.0488 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `219.3`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `1360.9256 ms` | max frame `1233.0643 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `17.1511 ms`
- p95 tick: `38.2559 ms`
- max tick: `42.6021 ms`
- avg simulation: `10.1346 ms` | avg presentation: `6.8980 ms`
- avg presenter behavior: `0.1667 ms` | avg animator: `0.0007 ms` | avg emit: `3.1914 ms` | avg request flush: `2.3765 ms`
- hottest presentation system: `PresenterEmitSystem` avg `3.1975 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `27.9909 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0300 ms` | p95 culling: `0.0438 ms` | max culling: `0.0774 ms`
- avg HUD projection: `0.0244 ms` | p95 HUD projection: `0.0704 ms` | max HUD projection: `0.1259 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `58.3`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `2088.1642 ms` | max frame `1964.0680 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `40.1837 ms`
- p95 tick: `74.6198 ms`
- max tick: `81.4826 ms`
- avg simulation: `28.1415 ms` | avg presentation: `11.8949 ms`
- avg presenter behavior: `0.2975 ms` | avg animator: `0.0006 ms` | avg emit: `5.4172 ms` | avg request flush: `4.2836 ms`
- hottest presentation system: `PresenterEmitSystem` avg `5.4225 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `38.4548 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `30.0`/`105` | attr changes `30.0`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0355 ms` | p95 culling: `0.0424 ms` | max culling: `0.1135 ms`
- avg HUD projection: `0.0386 ms` | p95 HUD projection: `0.1187 ms` | max HUD projection: `0.1630 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `24.9`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `5224.9859 ms` | max frame `4972.1225 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `100.6540 ms`
- p95 tick: `190.8864 ms`
- max tick: `220.9027 ms`
- avg simulation: `77.6326 ms` | avg presentation: `22.8660 ms`
- avg presenter behavior: `0.4354 ms` | avg animator: `0.0007 ms` | avg emit: `10.4612 ms` | avg request flush: `8.4498 ms`
- hottest presentation system: `PresenterEmitSystem` avg `10.4247 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `97.0808 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `42.1`/`206` | attr changes `42.1`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0377 ms` | p95 culling: `0.0499 ms` | max culling: `0.0683 ms`
- avg HUD projection: `0.0499 ms` | p95 HUD projection: `0.1975 ms` | max HUD projection: `0.2561 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `9.9`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `34383.7505 ms` | max frame `33441.4479 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `300.8707 ms`
- p95 tick: `718.2097 ms`
- max tick: `1205.0156 ms`
- avg simulation: `221.5615 ms` | avg presentation: `79.1641 ms`
- avg presenter behavior: `0.4107 ms` | avg animator: `0.0006 ms` | avg emit: `32.7049 ms` | avg request flush: `36.0488 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `37.1590 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `235.4657 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0402 ms` | p95 culling: `0.0539 ms` | max culling: `0.1278 ms`
- avg HUD projection: `0.0375 ms` | p95 HUD projection: `0.4413 ms` | max HUD projection: `0.5874 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `3.3`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `34536.7798 ms` | max frame `34049.7837 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `214.3421 ms`
- p95 tick: `588.1220 ms`
- max tick: `615.2062 ms`
- avg simulation: `190.7061 ms` | avg presentation: `23.5349 ms`
- avg presenter behavior: `0.3649 ms` | avg animator: `0.0007 ms` | avg emit: `13.2010 ms` | avg request flush: `1.5021 ms`
- hottest presentation system: `PresenterEmitSystem` avg `13.2023 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `202.1567 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0350 ms` | p95 culling: `0.0430 ms` | max culling: `0.0577 ms`
- avg HUD projection: `0.0063 ms` | p95 HUD projection: `0.0210 ms` | max HUD projection: `0.0279 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `4.7`

