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
- initialization: frames `9` | total `34.1613 ms` | max frame `17.3640 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.2402 ms`
- p95 tick: `0.5304 ms`
- max tick: `0.9879 ms`
- avg simulation: `0.0826 ms` | avg presentation: `0.1322 ms`
- avg presenter behavior: `0.0045 ms` | avg animator: `0.0003 ms` | avg emit: `0.0491 ms` | avg request flush: `0.0318 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0496 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.0870 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0067 ms` | p95 culling: `0.0146 ms` | max culling: `0.0259 ms`
- avg HUD projection: `0.0010 ms` | p95 HUD projection: `0.0048 ms` | max HUD projection: `0.0081 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `4163.9`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `80.2010 ms` | max frame `68.8239 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.6345 ms`
- p95 tick: `1.2829 ms`
- max tick: `1.4277 ms`
- avg simulation: `0.1484 ms` | avg presentation: `0.4484 ms`
- avg presenter behavior: `0.0127 ms` | avg animator: `0.0004 ms` | avg emit: `0.1979 ms` | avg request flush: `0.1357 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.1988 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1727 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0100 ms` | p95 culling: `0.0192 ms` | max culling: `0.0481 ms`
- avg HUD projection: `0.0026 ms` | p95 HUD projection: `0.0084 ms` | max HUD projection: `0.0109 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `1576.1`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `783.0191 ms` | max frame `668.6664 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `4.5746 ms`
- p95 tick: `8.1084 ms`
- max tick: `9.7487 ms`
- avg simulation: `0.6969 ms` | avg presentation: `3.8174 ms`
- avg presenter behavior: `0.0777 ms` | avg animator: `0.0004 ms` | avg emit: `1.8342 ms` | avg request flush: `1.3641 ms`
- hottest presentation system: `PresenterEmitSystem` avg `1.8379 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `1.0726 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0136 ms` | p95 culling: `0.0250 ms` | max culling: `0.0322 ms`
- avg HUD projection: `0.0103 ms` | p95 HUD projection: `0.0379 ms` | max HUD projection: `0.0485 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `218.6`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `1945.0233 ms` | max frame `1805.2748 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `13.1296 ms`
- p95 tick: `18.6510 ms`
- max tick: `23.0076 ms`
- avg simulation: `1.8694 ms` | avg presentation: `11.1531 ms`
- avg presenter behavior: `0.2082 ms` | avg animator: `0.0008 ms` | avg emit: `5.4298 ms` | avg request flush: `4.0334 ms`
- hottest presentation system: `PresenterEmitSystem` avg `5.4474 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `2.8774 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0209 ms` | p95 culling: `0.0320 ms` | max culling: `0.0411 ms`
- avg HUD projection: `0.0280 ms` | p95 HUD projection: `0.0873 ms` | max HUD projection: `0.1047 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `76.2`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `3995.9612 ms` | max frame `3801.8672 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `22.3442 ms`
- p95 tick: `28.4815 ms`
- max tick: `35.4459 ms`
- avg simulation: `3.2807 ms` | avg presentation: `18.9264 ms`
- avg presenter behavior: `0.3737 ms` | avg animator: `0.0008 ms` | avg emit: `9.0605 ms` | avg request flush: `7.0091 ms`
- hottest presentation system: `PresenterEmitSystem` avg `9.0832 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `2.4347 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `29.4`/`105` | attr changes `29.4`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0249 ms` | p95 culling: `0.0341 ms` | max culling: `0.0360 ms`
- avg HUD projection: `0.0419 ms` | p95 HUD projection: `0.1259 ms` | max HUD projection: `0.1393 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `44.8`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `10572.5445 ms` | max frame `10095.1136 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `59.4944 ms`
- p95 tick: `72.9378 ms`
- max tick: `343.5956 ms`
- avg simulation: `8.0324 ms` | avg presentation: `51.2332 ms`
- avg presenter behavior: `0.7086 ms` | avg animator: `0.0014 ms` | avg emit: `24.0976 ms` | avg request flush: `19.1858 ms`
- hottest presentation system: `PresenterEmitSystem` avg `24.3177 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `4.1713 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `40.8`/`206` | attr changes `40.8`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0414 ms` | p95 culling: `0.0543 ms` | max culling: `0.0714 ms`
- avg HUD projection: `0.0713 ms` | p95 HUD projection: `0.3152 ms` | max HUD projection: `0.3380 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `16.8`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `75282.8722 ms` | max frame `74333.1633 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `128.8223 ms`
- p95 tick: `160.5102 ms`
- max tick: `787.4403 ms`
- avg simulation: `12.1103 ms` | avg presentation: `116.5417 ms`
- avg presenter behavior: `0.4504 ms` | avg animator: `0.0013 ms` | avg emit: `54.8076 ms` | avg request flush: `46.4052 ms`
- hottest presentation system: `PresenterEmitSystem` avg `54.9252 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `5.7331 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0323 ms` | p95 culling: `0.0422 ms` | max culling: `0.0679 ms`
- avg HUD projection: `0.0422 ms` | p95 HUD projection: `0.4924 ms` | max HUD projection: `0.5502 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `7.8`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `50216.0284 ms` | max frame `49895.4846 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `51.1101 ms`
- p95 tick: `79.3466 ms`
- max tick: `808.3115 ms`
- avg simulation: `13.5750 ms` | avg presentation: `37.4109 ms`
- avg presenter behavior: `0.4790 ms` | avg animator: `0.0011 ms` | avg emit: `19.6927 ms` | avg request flush: `3.0574 ms`
- hottest presentation system: `PresenterEmitSystem` avg `19.7019 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `6.2176 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0312 ms` | p95 culling: `0.0443 ms` | max culling: `0.0783 ms`
- avg HUD projection: `0.0100 ms` | p95 HUD projection: `0.0345 ms` | max HUD projection: `0.0539 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `19.6`

