# Scenario Card: mass-navigation-large-world

## Intent
- Player goal: verify MassNavigation is the MassNavigationFlow SSOT and runs through performer + core minimap on a 64km RTS map.
- Gameplay domain: real launcher bootstrap, component-authored MassNavigation binding, EntityCollectionStore command source, OrderBuffer, performer runtime and core MinimapRuntime.

## Determinism Inputs
- Build: `1.0.0.0`
- Execution timestamp UTC: `2026-07-11T00:16:32.2696873+00:00`
- Map: `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch $capability_standard_mass_navigation_large_world_10k --adapter raylib --record D:\003_Ludots\Ludots_issue642_massnav\artifacts\acceptance\mass-navigation-issue-642\runs\20260711-081353\run-0001`
- Evidence images: `screens/000_boot.png`, `screens/001_selection_order.png`, `screens/002_remote_minimap_jump.png`, `screens/003_return_original_area.png`, `screens/timeline.png`

## Action Script
1. Boot the real MassNavigation launcher preset and wait for core MassNavigation runtime binding to settle.
2. Seed `collection.command.source` through EntityCollectionStore and submit a `massNavigationMove` order through OrderBufferSystem.
3. Jump the core minimap camera to a remote 64km hot-zone landmark, then jump back to the original area.
4. Disable MassNavigation timing and presentation system-breakdown timing, warm up for 300 ticks, then run a 60s wall-clock steady-state measurement.
5. Fail if units are recreated/reset, performer payloads are missing, minimap markers drop, core minimap is not the visible RTS full-map preset, or agent storage grows during steady state.

## Timeline
- [000_boot] camera=0,0 agents=10000 teams=4 commandActors=0 groups=0/0 performers=30009 minimap=10000/10009 worldHud=0 screenHud=0/0/drop:0 loadedChunks=36 frame=27.309ms sim=0.002ms pres=27.216ms mass_navigation=0.000ms
- [001_selection_order] camera=0,0 agents=10000 teams=4 commandActors=128 groups=0/0 performers=30145 minimap=10000/10009 worldHud=20000 screenHud=10000/10000/drop:0 loadedChunks=42 frame=36.944ms sim=0.001ms pres=36.897ms mass_navigation=32.539ms
- [002_remote_minimap_jump] camera=2400000,2100000 agents=10000 teams=4 commandActors=128 groups=0/0 performers=30145 minimap=10000/10009 worldHud=20000 screenHud=0/0/drop:0 loadedChunks=42 frame=16.237ms sim=0.001ms pres=16.203ms mass_navigation=10.377ms
- [003_return_original_area] camera=0,0 agents=10000 teams=4 commandActors=128 groups=0/0 performers=30145 minimap=10000/10009 worldHud=20000 screenHud=9978/9979/drop:0 loadedChunks=42 frame=16.201ms sim=0.001ms pres=16.161ms mass_navigation=9.768ms

## Outcome
- success: yes
- verdict: MassNavigation passes large-world performer/minimap/avoidance UAT and 60s timing-disabled steady-state evidence with 10000 agents, 30009 performers, 10009 minimap markers and zero agent-storage growth.

## Summary Stats
- world: `6400000 x 6400000` cm
- agents: `10000`
- blockers: `0`
- hotspot markers: `4`
- performer active at boot: `30009`
- minimap markers at boot: `10009` droppedTotal=`0`
- world/screen HUD after order: `20000` / bars=`10000` texts=`10000` droppedTotal=`0`
- initial submitted orders / moved command actors: `128` / `128`
- scenario spawn count boot/final: `2` / `2`
- scene reset count boot/final: `1` / `1`
- avoidance frames: `181`
- avoidance max visible/play-area/selected/heavy-profile agents: `2500` / `2345` / `128` / `339`
- avoidance peak/final deep overlap pairs: `6665` / `0`
- avoidance peak/final max penetration ratio: `99.77%` / `3.98%`
- median headless tick: `27.792ms`
- max headless tick: `3703.456ms`
- steady-state timing disabled: `True`
- steady-state requested/measured duration: `60s` / `60.050s`
- steady-state ticks/orders average/max: `2317` / `12` / `24.282ms` / `297.353ms`
- steady-state throughput: `38.585` headless ticks/s
- process-wide total allocated bytes: `5454600`; `90834.911` bytes/s; `2354.165` bytes/tick (includes the runtime and evidence host; not a MassNavigation-only allocation claim)
- retained managed growth after full GC: `-214168` bytes
- GC collections gen0/gen1/gen2: `0` / `0` / `0`
- working set start/end/sampled-peak/growth: `1889894400` / `1895223296` / `1896697856` / `5328896` bytes (peak sampled every 1s)
- managed heap start/end/growth: `1153128760` / `1157025784` / `3897024` bytes
- heap fragmentation start/end: `20919440` / `25026600` bytes
- steady agents/spawns/resets start/end: `10000/10000` / `2/2` / `1/1`
- steady active groups/order groups/settled agents start/end: `0/0` / `0/0` / `9999/9999`
- prepared agent capacity start/end: `10000` / `10000`
- agent storage allocation count start/end/growth: `1` / `1` / `0`
- normalized signature: `mass_navigation_large_world|agents:10000|teams:4|performers:30009|markers:10009/0|orders:128/moved:128|remote:2400000,2100000|spawns:2->2|resets:1->1|avoidance:181/2500/339/0/0.0398|steady:60s/capacity-growth:0`
- reusable wiring: `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`, `SystemGroup.RuntimeEntityBinding`, `EntityCollectionStore`, `OrderBufferSystem`, `PerformerEntityRuntime`, `MinimapRuntime`, `PresentationTimingDiagnostics`
