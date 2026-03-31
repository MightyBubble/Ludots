# Scenario Card: navigation2d-bottleneck-obstacle

## Intent
- Player goal: verify the launcher-started Navigation2D playground actually decongests over time instead of timing out as a stationary knot in the center.
- Gameplay domain: real launcher bootstrap, real adapter camera and culling services, real Navigation2D playground scenario state.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib --record artifacts/acceptance/navigation2d/bottleneck_obstacle`
- Scenario: `Bottleneck`
- Agents per team: `64`
- Clock profile: fixed `1/60s`, timeout tick `720`
- Evidence images: `screens/000_start.png`, `screens/120_t120.png`, `screens/240_t240.png`, `screens/360_t360.png`, `screens/480_t480.png`, `screens/600_t600.png`, `screens/720_t720.png`, `screens/timeline.png`

## Action Script
1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.
2. Force the `Bottleneck` scenario and deterministic agent count through the existing playground state.
3. Simulate until timeout while sampling crowd progress every 30 ticks and capturing timeline frames every 120 ticks.
4. Fail if timeout still looks like a dense stationary center jam.

## Expected Outcomes
- Primary success condition: both teams measurably advance through the conflict zone and timeout no longer shows a dense stationary center jam.
- Failure branch condition: timeout arrives with weak median progress, excessive center occupancy, or too many stationary agents trapped in the center box.
- Key metrics: team median X progress, center occupancy, stopped center agents, moving agent count, crossed fractions.

## Timeline
- [T+000] 000_start | MedianX T0=-9620 T1=9620 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=0 | Tick=6.103ms
- [T+120] 120_t120 | MedianX T0=-8138 T1=8251 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=126 | Tick=0.513ms
- [T+240] 240_t240 | MedianX T0=-6174 T1=6128 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.141ms
- [T+360] 360_t360 | MedianX T0=-3856 T1=3834 | DirectionalCross T0=0% T1=0% | Center=14 move=14 stop=0 | Moving=128 | Tick=0.124ms
- [T+480] 480_t480 | MedianX T0=-1498 T1=1553 | DirectionalCross T0=20% T1=20% | Center=44 move=41 stop=3 | Moving=125 | Tick=0.089ms
- [T+600] 600_t600 | MedianX T0=-242 T1=231 | DirectionalCross T0=38% T1=30% | Center=98 move=84 stop=14 | Moving=114 | Tick=0.087ms
- [T+720] 720_t720 | MedianX T0=96 T1=171 | DirectionalCross T0=61% T1=38% | Center=93 move=58 stop=35 | Moving=93 | Tick=0.070ms

## Outcome
- success: yes
- verdict: Timed avoidance passes: median advance is 9716/9449cm and timeout center occupancy is 93/128 with 35 stationary.
- reason: median advance reached `9716` / `9449` cm; timeout center box held `93` of `128` agents with `35` stationary; peak center occupancy was `98` at tick `600`.

## Summary Stats
- trace samples: `25`
- screenshot captures: `7`
- median headless tick: `1.752ms`
- max headless tick: `600.400ms`
- normalized signature: `navigation2d-bottleneck-obstacle|mid:5764/5786|final:9716/9449|center:93/128|stopped:35|peak:98@600`
- reusable wiring: `launcher.runtime.json`, `Navigation2DPlaygroundState`, `Navigation2DRuntime`, `ScreenOverlayBuffer`, `PlayerInputHandler`
