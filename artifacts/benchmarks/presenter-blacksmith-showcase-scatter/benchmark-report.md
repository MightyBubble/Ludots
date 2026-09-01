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
- initialization: frames `9` | total `12.7349 ms` | max frame `8.7179 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.1197 ms`
- p95 tick: `0.3009 ms`
- max tick: `0.6613 ms`
- avg simulation: `0.0437 ms` | avg presentation: `0.0641 ms`
- avg presenter behavior: `0.0022 ms` | avg animator: `0.0001 ms` | avg emit: `0.0244 ms` | avg request flush: `0.0164 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0245 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.0398 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0030 ms` | p95 culling: `0.0047 ms` | max culling: `0.0090 ms`
- avg HUD projection: `0.0005 ms` | p95 HUD projection: `0.0033 ms` | max HUD projection: `0.0074 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `8351.3`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `30.4469 ms` | max frame `25.2244 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.2988 ms`
- p95 tick: `0.5603 ms`
- max tick: `1.9035 ms`
- avg simulation: `0.0866 ms` | avg presentation: `0.1984 ms`
- avg presenter behavior: `0.0057 ms` | avg animator: `0.0001 ms` | avg emit: `0.0921 ms` | avg request flush: `0.0621 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0922 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1185 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0034 ms` | p95 culling: `0.0058 ms` | max culling: `0.0087 ms`
- avg HUD projection: `0.0010 ms` | p95 HUD projection: `0.0039 ms` | max HUD projection: `0.0121 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `3346.5`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `341.3998 ms` | max frame `290.8125 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `3.7858 ms`
- p95 tick: `6.2357 ms`
- max tick: `25.0527 ms`
- avg simulation: `1.6106 ms` | avg presentation: `2.1112 ms`
- avg presenter behavior: `0.0497 ms` | avg animator: `0.0006 ms` | avg emit: `0.9741 ms` | avg request flush: `0.7367 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.9746 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `3.4930 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0160 ms` | p95 culling: `0.0252 ms` | max culling: `0.0313 ms`
- avg HUD projection: `0.0101 ms` | p95 HUD projection: `0.0339 ms` | max HUD projection: `0.0405 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `264.1`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `1235.4507 ms` | max frame `1109.4741 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `17.0797 ms`
- p95 tick: `35.1488 ms`
- max tick: `45.3741 ms`
- avg simulation: `9.8545 ms` | avg presentation: `7.1040 ms`
- avg presenter behavior: `0.1534 ms` | avg animator: `0.0009 ms` | avg emit: `3.2310 ms` | avg request flush: `2.6178 ms`
- hottest presentation system: `PresenterEmitSystem` avg `3.2302 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `27.1766 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0291 ms` | p95 culling: `0.0394 ms` | max culling: `0.0473 ms`
- avg HUD projection: `0.0244 ms` | p95 HUD projection: `0.0692 ms` | max HUD projection: `0.0825 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `58.5`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `2269.1493 ms` | max frame `2150.9366 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `39.5952 ms`
- p95 tick: `68.0681 ms`
- max tick: `94.4068 ms`
- avg simulation: `26.7817 ms` | avg presentation: `12.6623 ms`
- avg presenter behavior: `0.3024 ms` | avg animator: `0.0012 ms` | avg emit: `5.6211 ms` | avg request flush: `4.8179 ms`
- hottest presentation system: `PresenterEmitSystem` avg `5.6254 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `36.5413 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `30.0`/`105` | attr changes `30.0`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0349 ms` | p95 culling: `0.0440 ms` | max culling: `0.0483 ms`
- avg HUD projection: `0.0394 ms` | p95 HUD projection: `0.1232 ms` | max HUD projection: `0.1616 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `25.3`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `5747.2458 ms` | max frame `5555.6137 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `102.0424 ms`
- p95 tick: `164.7522 ms`
- max tick: `191.1168 ms`
- avg simulation: `77.6525 ms` | avg presentation: `24.2294 ms`
- avg presenter behavior: `0.4304 ms` | avg animator: `0.0011 ms` | avg emit: `10.8373 ms` | avg request flush: `9.5440 ms`
- hottest presentation system: `PresenterEmitSystem` avg `10.8172 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `91.9909 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `45.0`/`206` | attr changes `45.0`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0366 ms` | p95 culling: `0.0467 ms` | max culling: `0.0849 ms`
- avg HUD projection: `0.0517 ms` | p95 HUD projection: `0.1950 ms` | max HUD projection: `0.2604 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `9.8`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `51596.9315 ms` | max frame `50969.8172 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `282.3279 ms`
- p95 tick: `674.8510 ms`
- max tick: `844.9126 ms`
- avg simulation: `201.5097 ms` | avg presentation: `80.6751 ms`
- avg presenter behavior: `0.3968 ms` | avg animator: `0.0011 ms` | avg emit: `34.0308 ms` | avg request flush: `36.8810 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `37.2863 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `213.8268 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0363 ms` | p95 culling: `0.0491 ms` | max culling: `0.0544 ms`
- avg HUD projection: `0.0349 ms` | p95 HUD projection: `0.4294 ms` | max HUD projection: `0.5183 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `3.5`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `37742.8923 ms` | max frame `37533.0402 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `414.7548 ms`
- p95 tick: `1290.5075 ms`
- max tick: `2370.4636 ms`
- avg simulation: `377.8815 ms` | avg presentation: `36.7429 ms`
- avg presenter behavior: `0.5797 ms` | avg animator: `0.0014 ms` | avg emit: `19.1055 ms` | avg request flush: `2.7457 ms`
- hottest presentation system: `PresenterEmitSystem` avg `18.8533 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `403.0402 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0412 ms` | p95 culling: `0.0598 ms` | max culling: `0.0835 ms`
- avg HUD projection: `0.0082 ms` | p95 HUD projection: `0.0342 ms` | max HUD projection: `0.0612 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `2.4`

