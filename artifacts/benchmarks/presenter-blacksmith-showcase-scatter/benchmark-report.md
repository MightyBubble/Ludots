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
- initialization: frames `9` | total `222.4237 ms` | max frame `208.0131 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.1385 ms`
- p95 tick: `0.3601 ms`
- max tick: `0.4643 ms`
- avg simulation: `0.0504 ms` | avg presentation: `0.0765 ms`
- avg presenter behavior: `0.0032 ms` | avg animator: `0.0002 ms` | avg emit: `0.0296 ms` | avg request flush: `0.0143 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.0297 ms`
- hottest simulation system: `Physics2DSimulationSystem` avg `0.0178 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0033 ms` | p95 culling: `0.0055 ms` | max culling: `0.0080 ms`
- avg HUD projection: `0.0007 ms` | p95 HUD projection: `0.0035 ms` | max HUD projection: `0.0055 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `7220.0`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `68.9491 ms` | max frame `58.4148 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.4186 ms`
- p95 tick: `0.8781 ms`
- max tick: `1.4997 ms`
- avg simulation: `0.1228 ms` | avg presentation: `0.2723 ms`
- avg presenter behavior: `0.0128 ms` | avg animator: `0.0002 ms` | avg emit: `0.1311 ms` | avg request flush: `0.0641 ms`
- hottest presentation system: `PresenterEmitSystem` avg `0.1315 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1653 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0059 ms` | p95 culling: `0.0100 ms` | max culling: `0.0366 ms`
- avg HUD projection: `0.0024 ms` | p95 HUD projection: `0.0110 ms` | max HUD projection: `0.0181 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `2388.7`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `1251.5901 ms` | max frame `1132.6086 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `5.7370 ms`
- p95 tick: `9.4082 ms`
- max tick: `20.8900 ms`
- avg simulation: `1.4022 ms` | avg presentation: `4.2368 ms`
- avg presenter behavior: `0.1426 ms` | avg animator: `0.0007 ms` | avg emit: `2.1597 ms` | avg request flush: `1.3939 ms`
- hottest presentation system: `PresenterEmitSystem` avg `2.1688 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `2.1918 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0184 ms` | p95 culling: `0.0291 ms` | max culling: `0.0514 ms`
- avg HUD projection: `0.0129 ms` | p95 HUD projection: `0.0389 ms` | max HUD projection: `0.0628 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `174.3`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `2889.7911 ms` | max frame `2770.7132 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `6.3834 ms`
- p95 tick: `10.6821 ms`
- max tick: `12.4406 ms`
- avg simulation: `0.9661 ms` | avg presentation: `5.3184 ms`
- avg presenter behavior: `0.1435 ms` | avg animator: `0.0006 ms` | avg emit: `2.1164 ms` | avg request flush: `2.3769 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `2.3496 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `1.4171 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0175 ms` | p95 culling: `0.0259 ms` | max culling: `0.0762 ms`
- avg HUD projection: `0.0284 ms` | p95 HUD projection: `0.1008 ms` | max HUD projection: `0.1195 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `156.7`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `2866.3156 ms` | max frame `2789.3611 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `9.5507 ms`
- p95 tick: `13.4911 ms`
- max tick: `16.3150 ms`
- avg simulation: `0.9878 ms` | avg presentation: `8.4845 ms`
- avg presenter behavior: `0.2154 ms` | avg animator: `0.0005 ms` | avg emit: `2.9934 ms` | avg request flush: `4.2420 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `4.2620 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.5049 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `27.9`/`105` | attr changes `27.9`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0122 ms` | p95 culling: `0.0169 ms` | max culling: `0.0287 ms`
- avg HUD projection: `0.0236 ms` | p95 HUD projection: `0.0963 ms` | max HUD projection: `0.1028 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `104.7`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `4522.3739 ms` | max frame `4380.2339 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `18.4037 ms`
- p95 tick: `24.0074 ms`
- max tick: `175.0870 ms`
- avg simulation: `3.1308 ms` | avg presentation: `15.1744 ms`
- avg presenter behavior: `0.4277 ms` | avg animator: `0.0005 ms` | avg emit: `5.1360 ms` | avg request flush: `7.6132 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `7.6324 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.5032 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `62.8`/`206` | attr changes `62.8`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0130 ms` | p95 culling: `0.0184 ms` | max culling: `0.0209 ms`
- avg HUD projection: `0.0428 ms` | p95 HUD projection: `0.1651 ms` | max HUD projection: `0.1863 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `54.3`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `22538.5782 ms` | max frame `22129.9462 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `74.3428 ms`
- p95 tick: `88.3362 ms`
- max tick: `715.2844 ms`
- avg simulation: `8.3886 ms` | avg presentation: `65.8588 ms`
- avg presenter behavior: `0.4201 ms` | avg animator: `0.0006 ms` | avg emit: `21.1470 ms` | avg request flush: `37.5803 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `37.5814 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `2.5898 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0165 ms` | p95 culling: `0.0230 ms` | max culling: `0.0305 ms`
- avg HUD projection: `0.0255 ms` | p95 HUD projection: `0.2844 ms` | max HUD projection: `0.4977 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `13.5`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `26619.9516 ms` | max frame `26433.7673 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `27.0792 ms`
- p95 tick: `38.7786 ms`
- max tick: `665.1183 ms`
- avg simulation: `7.8849 ms` | avg presentation: `19.1155 ms`
- avg presenter behavior: `0.3681 ms` | avg animator: `0.0008 ms` | avg emit: `9.8320 ms` | avg request flush: `2.1672 ms`
- hottest presentation system: `PresenterEmitSystem` avg `9.8285 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `2.1427 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0167 ms` | p95 culling: `0.0228 ms` | max culling: `0.0474 ms`
- avg HUD projection: `0.0038 ms` | p95 HUD projection: `0.0242 ms` | max HUD projection: `0.0306 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `36.9`

