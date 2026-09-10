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
- initialization: frames `9` | total `18.0929 ms` | max frame `9.6413 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `25`
- visible blacksmith entities: `25`
- presenters: root `25` | left `25` | right `25` | chimney `25` | route `25` | decal `25` | worker `25` | bar `25` | text `25`
- presentation: workshop primitives `50` | chimney primitives `25` | HUD bars `25` | HUD text `25` | splines `25` | overlays `25`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.0651 ms`
- p95 tick: `0.1526 ms`
- max tick: `0.3735 ms`
- avg simulation: `0.0217 ms` | avg presentation: `0.0370 ms`
- avg presenter behavior: `0.0010 ms` | avg animator: `0.0001 ms` | avg emit: `0.0092 ms` | avg request flush: `0.0136 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `0.0137 ms`
- hottest simulation system: `Physics2DSimulationSystem` avg `0.0056 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.5`/`3` | attr changes `0.5`/`3` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0014 ms` | p95 culling: `0.0021 ms` | max culling: `0.0044 ms`
- avg HUD projection: `0.0005 ms` | p95 HUD projection: `0.0021 ms` | max HUD projection: `0.0128 ms`
- visible entities avg/max: `25.0` / `25`
- primitive instances avg/max: `125.0` / `125`
- avg fps equivalent: `15360.4`

## scatter_100

- seed: `97531864`
- total buildings: `100`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `99`
- initialization: frames `9` | total `36.4694 ms` | max frame `32.5413 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `100`
- visible blacksmith entities: `100`
- presenters: root `100` | left `100` | right `100` | chimney `100` | route `100` | decal `100` | worker `100` | bar `100` | text `100`
- presentation: workshop primitives `200` | chimney primitives `100` | HUD bars `100` | HUD text `100` | splines `100` | overlays `100`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `0.2231 ms`
- p95 tick: `0.4058 ms`
- max tick: `0.4867 ms`
- avg simulation: `0.0524 ms` | avg presentation: `0.1570 ms`
- avg presenter behavior: `0.0048 ms` | avg animator: `0.0001 ms` | avg emit: `0.0485 ms` | avg request flush: `0.0696 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `0.0697 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.0616 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `0.9`/`7` | attr changes `0.9`/`7` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0029 ms` | p95 culling: `0.0042 ms` | max culling: `0.0065 ms`
- avg HUD projection: `0.0014 ms` | p95 HUD projection: `0.0055 ms` | max HUD projection: `0.0088 ms`
- visible entities avg/max: `100.0` / `100`
- primitive instances avg/max: `500.0` / `500`
- avg fps equivalent: `4482.5`

## scatter_1000

- seed: `41592653`
- total buildings: `1000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `999`
- initialization: frames `9` | total `197.4226 ms` | max frame `170.1736 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `1000`
- visible blacksmith entities: `1000`
- presenters: root `1000` | left `1000` | right `1000` | chimney `1000` | route `1000` | decal `1000` | worker `1000` | bar `1000` | text `1000`
- presentation: workshop primitives `2000` | chimney primitives `1000` | HUD bars `1000` | HUD text `1000` | splines `1000` | overlays `1000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `1.1948 ms`
- p95 tick: `1.9221 ms`
- max tick: `2.3914 ms`
- avg simulation: `0.1354 ms` | avg presentation: `1.0427 ms`
- avg presenter behavior: `0.0287 ms` | avg animator: `0.0001 ms` | avg emit: `0.3574 ms` | avg request flush: `0.5345 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `0.5347 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.1820 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `5.9`/`26` | attr changes `5.9`/`26` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0030 ms` | p95 culling: `0.0055 ms` | max culling: `0.0093 ms`
- avg HUD projection: `0.0051 ms` | p95 HUD projection: `0.0205 ms` | max HUD projection: `0.0269 ms`
- visible entities avg/max: `1000.0` / `1000`
- primitive instances avg/max: `5000.0` / `5000`
- avg fps equivalent: `836.9`

## scatter_3000_tight

- seed: `14142135`
- total buildings: `3000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `2999`
- initialization: frames `9` | total `970.7549 ms` | max frame `880.4369 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `3000`
- visible blacksmith entities: `3000`
- presenters: root `3000` | left `3000` | right `3000` | chimney `3000` | route `3000` | decal `3000` | worker `3000` | bar `3000` | text `3000`
- presentation: workshop primitives `6000` | chimney primitives `3000` | HUD bars `3000` | HUD text `3000` | splines `3000` | overlays `3000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `3.4459 ms`
- p95 tick: `5.6142 ms`
- max tick: `6.5816 ms`
- avg simulation: `0.4759 ms` | avg presentation: `2.9266 ms`
- avg presenter behavior: `0.1012 ms` | avg animator: `0.0004 ms` | avg emit: `0.9584 ms` | avg request flush: `1.4851 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `1.4854 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.5551 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `16.9`/`68` | attr changes `16.9`/`68` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0082 ms` | p95 culling: `0.0121 ms` | max culling: `0.0141 ms`
- avg HUD projection: `0.0152 ms` | p95 HUD projection: `0.0591 ms` | max HUD projection: `0.0704 ms`
- visible entities avg/max: `3000.0` / `3000`
- primitive instances avg/max: `15000.0` / `15000`
- avg fps equivalent: `290.2`

## scatter_5000

- seed: `27182818`
- total buildings: `5000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `4999`
- initialization: frames `9` | total `2119.1067 ms` | max frame `2054.0635 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `5000`
- visible blacksmith entities: `5000`
- presenters: root `5000` | left `5000` | right `5000` | chimney `5000` | route `5000` | decal `5000` | worker `5000` | bar `5000` | text `5000`
- presentation: workshop primitives `10000` | chimney primitives `5000` | HUD bars `5000` | HUD text `5000` | splines `5000` | overlays `5000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `7.4556 ms`
- p95 tick: `11.3359 ms`
- max tick: `12.4578 ms`
- avg simulation: `0.9438 ms` | avg presentation: `6.4425 ms`
- avg presenter behavior: `0.2025 ms` | avg animator: `0.0005 ms` | avg emit: `2.0717 ms` | avg request flush: `3.3381 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `3.3387 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.4052 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `27.9`/`105` | attr changes `27.9`/`105` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0115 ms` | p95 culling: `0.0160 ms` | max culling: `0.0193 ms`
- avg HUD projection: `0.0278 ms` | p95 HUD projection: `0.0999 ms` | max HUD projection: `0.1133 ms`
- visible entities avg/max: `5000.0` / `5000`
- primitive instances avg/max: `25000.0` / `25000`
- avg fps equivalent: `134.1`

## scatter_10000_tight

- seed: `17320508`
- total buildings: `10000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `9999`
- initialization: frames `9` | total `6167.7741 ms` | max frame `6045.1972 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `10000`
- visible blacksmith entities: `10000`
- presenters: root `10000` | left `10000` | right `10000` | chimney `10000` | route `10000` | decal `10000` | worker `10000` | bar `10000` | text `10000`
- presentation: workshop primitives `20000` | chimney primitives `10000` | HUD bars `10000` | HUD text `10000` | splines `10000` | overlays `10000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `17.1305 ms`
- p95 tick: `25.8336 ms`
- max tick: `135.0552 ms`
- avg simulation: `2.9151 ms` | avg presentation: `14.1015 ms`
- avg presenter behavior: `0.4900 ms` | avg animator: `0.0006 ms` | avg emit: `4.4395 ms` | avg request flush: `7.2556 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `7.2640 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `0.6053 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `62.8`/`206` | attr changes `62.8`/`206` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0142 ms` | p95 culling: `0.0197 ms` | max culling: `0.0229 ms`
- avg HUD projection: `0.0557 ms` | p95 HUD projection: `0.1897 ms` | max HUD projection: `0.2402 ms`
- visible entities avg/max: `10000.0` / `10000`
- primitive instances avg/max: `50000.0` / `50000`
- avg fps equivalent: `58.4`

## scatter_30000_tight

- seed: `31415926`
- total buildings: `30000`
- scatter radius cm: `750` -> `2400`
- full visibility expected: `True`
- queued extras: `29999`
- initialization: frames `9` | total `56106.1521 ms` | max frame `55751.8302 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `30000`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `60000` | chimney primitives `30000` | HUD bars `30000` | HUD text `30000` | splines `30000` | overlays `30000`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `47.8304 ms`
- p95 tick: `60.2383 ms`
- max tick: `409.1977 ms`
- avg simulation: `4.8706 ms` | avg presentation: `42.8696 ms`
- avg presenter behavior: `0.2956 ms` | avg animator: `0.0007 ms` | avg emit: `12.8936 ms` | avg request flush: `24.2878 ms`
- hottest presentation system: `PresentationRequestFlushSystem` avg `24.2901 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `1.5646 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0155 ms` | p95 culling: `0.0204 ms` | max culling: `0.0284 ms`
- avg HUD projection: `0.0245 ms` | p95 HUD projection: `0.2587 ms` | max HUD projection: `0.4473 ms`
- visible entities avg/max: `30000.0` / `30000`
- primitive instances avg/max: `150000.0` / `150000`
- avg fps equivalent: `20.9`

## scatter_30000_wide

- seed: `16180339`
- total buildings: `30000`
- scatter radius cm: `5000` -> `12000`
- full visibility expected: `False`
- queued extras: `29999`
- initialization: frames `9` | total `46517.6672 ms` | max frame `46408.4619 ms` | queue after settle `0` | stable settle `True`
- blacksmith entities: `30000`
- visible blacksmith entities: `2095`
- presenters: root `30000` | left `30000` | right `30000` | chimney `30000` | route `30000` | decal `30000` | worker `30000` | bar `30000` | text `30000`
- presentation: workshop primitives `4190` | chimney primitives `2095` | HUD bars `2095` | HUD text `2095` | splines `2095` | overlays `2095`
- drops: events `0` | commands `0` | primitives `0` | world HUD `0` | screen HUD `0` | skinned `0`
- avg tick: `17.7225 ms`
- p95 tick: `26.8294 ms`
- max tick: `342.9373 ms`
- avg simulation: `4.4268 ms` | avg presentation: `13.2370 ms`
- avg presenter behavior: `0.2512 ms` | avg animator: `0.0007 ms` | avg emit: `7.0629 ms` | avg request flush: `1.3872 ms`
- hottest presentation system: `PresenterEmitSystem` avg `7.0589 ms`
- hottest simulation system: `EffectProcessingLoopSystem` avg `1.4666 ms`
- presenter behavior counts avg/max: bootstrap `0.0`/`0` | owner changes `33.9`/`538` | attr changes `33.9`/`538` | tag changes `0.0`/`0`
- presenter behavior counts avg/max: tick-driven `0.0`/`0` | active sound tracking `0.0`/`0` | destroy-scan `0.0`/`0`
- avg culling: `0.0141 ms` | p95 culling: `0.0186 ms` | max culling: `0.0253 ms`
- avg HUD projection: `0.0043 ms` | p95 HUD projection: `0.0134 ms` | max HUD projection: `0.0193 ms`
- visible entities avg/max: `2095.0` / `2095`
- primitive instances avg/max: `10475.0` / `10475`
- avg fps equivalent: `56.4`

