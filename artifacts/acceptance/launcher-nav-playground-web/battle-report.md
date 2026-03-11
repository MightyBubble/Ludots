# Scenario Card: navigation2d-playground-timed-avoidance

## Intent
- Player goal: verify the launcher-started Navigation2D playground actually decongests over time instead of timing out as a stationary knot in the center.
- Gameplay domain: real launcher bootstrap, real adapter camera and culling services, real Navigation2D playground scenario state.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Adapter: `web`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter web --record artifacts/acceptance/launcher-nav-playground-web`
- Scenario: `Pass Through`
- Agents per team: `64`
- Clock profile: fixed `1/60s`, timeout tick `720`
- Evidence images: `screens/000_start.png`, `screens/120_t120.png`, `screens/240_t240.png`, `screens/360_t360.png`, `screens/480_t480.png`, `screens/600_t600.png`, `screens/720_t720.png`, `screens/timeline.png`

## Action Script
1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.
2. Force the Pass Through scenario and deterministic agent count through the existing playground state.
3. Simulate until timeout while sampling crowd progress every 30 ticks and capturing timeline frames every 120 ticks.
4. Fail if timeout still looks like a dense stationary center jam.

## Expected Outcomes
- Primary success condition: both teams measurably advance through the conflict zone and timeout no longer shows a dense stationary center jam.
- Failure branch condition: timeout arrives with weak median progress, excessive center occupancy, or too many stationary agents trapped in the center box.
- Key metrics: team median X progress, center occupancy, stopped center agents, moving agent count, crossed fractions.

## Timeline
- [T+000] 000_start | MedianX T0=-9420 T1=9420 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=0 | Tick=5.256ms
- [T+120] 120_t120 | MedianX T0=-9398 T1=9398 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=72 | Tick=8.663ms
- [T+240] 240_t240 | MedianX T0=-9056 T1=9056 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=1.978ms
- [T+360] 360_t360 | MedianX T0=-7942 T1=7942 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=2.027ms
- [T+480] 480_t480 | MedianX T0=-6687 T1=6687 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=1.935ms
- [T+600] 600_t600 | MedianX T0=-5450 T1=5449 | Crossed T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=1.786ms
- [T+720] 720_t720 | MedianX T0=-4117 T1=4115 | Crossed T0=0% T1=0% | Center=14 move=14 stop=0 | Moving=128 | Tick=3.050ms

## Outcome
- success: yes
- verdict: Timed avoidance passes: median advance is 5303/5305cm and timeout center occupancy is 14/128 with 0 stationary.
- reason: median advance reached `5303` / `5305` cm; timeout center box held `14` of `128` agents with `0` stationary; peak center occupancy was `14` at tick `720`.

## Summary Stats
- trace samples: `25`
- screenshot captures: `7`
- median headless tick: `2.466ms`
- max headless tick: `115.110ms`
- normalized signature: `navigation2d_playground_timed_avoidance|mid:1478/1478|final:5303/5305|center:14/128|stopped:0|peak:14@720`
- reusable wiring: `launcher.runtime.json`, `Navigation2DPlaygroundState`, `Navigation2DRuntime`, `ScreenOverlayBuffer`, `PlayerInputHandler`
