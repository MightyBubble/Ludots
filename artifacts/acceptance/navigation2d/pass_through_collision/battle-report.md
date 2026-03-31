# Scenario Card: navigation2d-pass-through-collision

## Intent
- Player goal: verify the launcher-started Navigation2D playground actually decongests over time instead of timing out as a stationary knot in the center.
- Gameplay domain: real launcher bootstrap, real adapter camera and culling services, real Navigation2D playground scenario state.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib --record artifacts/acceptance/navigation2d/pass_through_collision`
- Scenario: `Pass Through`
- Agents per team: `64`
- Clock profile: fixed `1/60s`, timeout tick `720`
- Evidence images: `screens/000_start.png`, `screens/120_t120.png`, `screens/240_t240.png`, `screens/360_t360.png`, `screens/480_t480.png`, `screens/600_t600.png`, `screens/720_t720.png`, `screens/timeline.png`

## Action Script
1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.
2. Force the `Pass Through` scenario and deterministic agent count through the existing playground state.
3. Simulate until timeout while sampling crowd progress every 30 ticks and capturing timeline frames every 120 ticks.
4. Fail if timeout still looks like a dense stationary center jam.

## Expected Outcomes
- Primary success condition: both teams measurably advance through the conflict zone and timeout no longer shows a dense stationary center jam.
- Failure branch condition: timeout arrives with weak median progress, excessive center occupancy, or too many stationary agents trapped in the center box.
- Key metrics: team median X progress, center occupancy, stopped center agents, moving agent count, crossed fractions.

## Timeline
- [T+000] 000_start | MedianX T0=-9420 T1=9420 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=0 | Tick=4.818ms
- [T+120] 120_t120 | MedianX T0=-7428 T1=7408 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.409ms
- [T+240] 240_t240 | MedianX T0=-4728 T1=4841 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.115ms
- [T+360] 360_t360 | MedianX T0=-1904 T1=2040 | DirectionalCross T0=3% T1=5% | Center=38 move=38 stop=0 | Moving=128 | Tick=0.083ms
- [T+480] 480_t480 | MedianX T0=982 T1=-721 | DirectionalCross T0=70% T1=69% | Center=76 move=76 stop=0 | Moving=128 | Tick=0.168ms
- [T+600] 600_t600 | MedianX T0=3996 T1=-3763 | DirectionalCross T0=100% T1=100% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.151ms
- [T+720] 720_t720 | MedianX T0=7055 T1=-6843 | DirectionalCross T0=100% T1=100% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.078ms

## Outcome
- success: yes
- verdict: Timed avoidance passes: median advance is 16475/16263cm and timeout center occupancy is 0/128 with 0 stationary.
- reason: median advance reached `16475` / `16263` cm; timeout center box held `0` of `128` agents with `0` stationary; peak center occupancy was `82` at tick `450`.

## Summary Stats
- trace samples: `25`
- screenshot captures: `7`
- median headless tick: `1.748ms`
- max headless tick: `553.296ms`
- normalized signature: `navigation2d-pass-through-collision|mid:7516/7380|final:16475/16263|center:0/128|stopped:0|peak:82@450`
- reusable wiring: `launcher.runtime.json`, `Navigation2DPlaygroundState`, `Navigation2DRuntime`, `ScreenOverlayBuffer`, `PlayerInputHandler`
