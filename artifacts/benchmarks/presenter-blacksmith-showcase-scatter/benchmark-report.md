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
- initialization: frames `9` | total `37.3191 ms` | max frame `25.0652 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.4865 ms`
- p95 tick: `1.0277 ms`
- max tick: `16.5441 ms`
- avg simulation: `0.2709 ms` | avg presentation: `0.1784 ms`
- avg presenter behavior: `0.0073 ms` | avg animator: `0.0003 ms` | avg emit: `0.0650 ms` | avg request flush: `0.0360 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0652 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1207 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0094 ms` | p95 culling: `0.0209 ms` | max culling: `0.0437 ms`
- avg HUD projection: `0.0019 ms` | p95 HUD projection: `0.0127 ms` | max HUD projection: `0.0171 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `2055.6`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `90.7963 ms` | max frame `70.5293 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.8074 ms`
- p95 tick: `1.7399 ms`
- max tick: `6.3936 ms`
- avg simulation: `0.2593 ms` | avg presentation: `0.5057 ms`
- avg presenter behavior: `0.0157 ms` | avg animator: `0.0003 ms` | avg emit: `0.2282 ms` | avg request flush: `0.1391 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.2290 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.3368 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0104 ms` | p95 culling: `0.0203 ms` | max culling: `0.0444 ms`
- avg HUD projection: `0.0028 ms` | p95 HUD projection: `0.0109 ms` | max HUD projection: `0.0217 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `1238.5`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `765.8962 ms` | max frame `641.4212 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `8.9912 ms`
- p95 tick: `16.0145 ms`
- max tick: `44.3545 ms`
- avg simulation: `3.6486 ms` | avg presentation: `5.2004 ms`
- avg presenter behavior: `0.1091 ms` | avg animator: `0.0011 ms` | avg emit: `2.3798 ms` | avg request flush: `1.7773 ms`
- hottest presentation system: `PresenterEmitSystem` avg `2.4492 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `8.4636 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0355 ms` | p95 culling: `0.0487 ms` | max culling: `0.1235 ms`
- avg HUD projection: `0.0183 ms` | p95 HUD projection: `0.0536 ms` | max HUD projection: `0.0673 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `111.2`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `2519.8587 ms` | max frame `2326.1101 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `40.0417 ms`
- p95 tick: `89.4413 ms`
- max tick: `93.3321 ms`
- avg simulation: `23.9118 ms` | avg presentation: `15.9425 ms`
- avg presenter behavior: `0.3349 ms` | avg animator: `0.0013 ms` | avg emit: `7.4168 ms` | avg request flush: `5.7136 ms`
- hottest presentation system: `PresenterEmitSystem` avg `7.4972 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `66.8278 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0418 ms` | p95 culling: `0.0545 ms` | max culling: `0.0836 ms`
- avg HUD projection: `0.0378 ms` | p95 HUD projection: `0.1133 ms` | max HUD projection: `0.2224 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `25.0`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `7662.6218 ms` | max frame `7339.4362 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `84.5396 ms`
- p95 tick: `148.2603 ms`
- max tick: `158.0953 ms`
- avg simulation: `58.7962 ms` | avg presentation: `25.5540 ms`
- avg presenter behavior: `0.4978 ms` | avg animator: `0.0012 ms` | avg emit: `11.7115 ms` | avg request flush: `9.6006 ms`
- hottest presentation system: `PresenterEmitSystem` avg `12.0102 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `83.0132 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `28.6`/`105` | attr changes `28.6`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0439 ms` | p95 culling: `0.0532 ms` | max culling: `0.0715 ms`
- avg HUD projection: `0.0486 ms` | p95 HUD projection: `0.1561 ms` | max HUD projection: `0.2253 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `11.8`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `14803.6869 ms` | max frame `14284.8464 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `130.0861 ms`
- p95 tick: `210.6069 ms`
- max tick: `1848.8284 ms`
- avg simulation: `98.7809 ms` | avg presentation: `31.1432 ms`
- avg presenter behavior: `0.4482 ms` | avg animator: `0.0009 ms` | avg emit: `12.2241 ms` | avg request flush: `14.3565 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `14.4264 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `106.2138 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `40.8`/`206` | attr changes `40.8`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0376 ms` | p95 culling: `0.0504 ms` | max culling: `0.0862 ms`
- avg HUD projection: `0.0504 ms` | p95 HUD projection: `0.2125 ms` | max HUD projection: `0.2524 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `7.7`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `93098.4356 ms` | max frame `91483.3492 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `388.2375 ms`
- p95 tick: `886.5763 ms`
- max tick: `1076.8533 ms`
- avg simulation: `270.9279 ms` | avg presentation: `117.1288 ms`
- avg presenter behavior: `0.5071 ms` | avg animator: `0.0013 ms` | avg emit: `44.2632 ms` | avg request flush: `59.2304 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `59.2420 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `286.5295 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0432 ms` | p95 culling: `0.0552 ms` | max culling: `0.0862 ms`
- avg HUD projection: `0.0447 ms` | p95 HUD projection: `0.4982 ms` | max HUD projection: `0.7330 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `2.6`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `98589.6420 ms` | max frame `97774.7800 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `346.2659 ms`
- p95 tick: `954.9711 ms`
- max tick: `1077.0877 ms`
- avg simulation: `308.1060 ms` | avg presentation: `38.0169 ms`
- avg presenter behavior: `0.5336 ms` | avg animator: `0.0010 ms` | avg emit: `20.0467 ms` | avg request flush: `3.4099 ms`
- hottest presentation system: `PresenterEmitSystem` avg `20.0707 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `326.8290 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0429 ms` | p95 culling: `0.0551 ms` | max culling: `0.0924 ms`
- avg HUD projection: `0.0150 ms` | p95 HUD projection: `0.1138 ms` | max HUD projection: `0.2307 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `2.9`

