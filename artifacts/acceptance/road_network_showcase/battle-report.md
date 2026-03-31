# Scenario Card: road-network-showcase-command-and-chunking

## Intent
- Player goal: select a road column, right-click a fort along the road network, see immediate command feedback, and watch chunk streaming react when the camera shifts east.
- Gameplay domain: real launcher bootstrap, real input mapping, real graph-only auto path service, real road spline performer, and real loaded-chunk window updates.

## Determinism Inputs
- Seed: none
- Map: `mods/showcases/road_network/RoadNetworkShowcaseMod/assets/Maps/road_network_showcase_chunked.json`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch road_network_showcase --adapter raylib --record artifacts/acceptance/road_network_showcase`
- Selection point: `-9800,0`
- Command target: `0,0`
- Chunk probe camera target: `18000,0`
- Clock profile: fixed `1/60s`
- Evidence images: `screens/000_start.png`, `screens/001_selected.png`, `screens/002_command_accepted.png`, `screens/003_column_advancing.png`, `screens/004_chunk_shifted.png`, `screens/timeline.png`

## Timeline
- [T+001] RoadShowcase.000_start -> status=Road command ready. Right-click near a road or fort. | selected=Blue Vanguard | controlled=Blue Vanguard -9800,0 | vanguard=-9800,0 | north=-9400,700 | south=-9400,-700 | chunks=25 | nodes=135 | roads=9 | cue=Off | camera=-9989,-189 | tick=5.542ms
- [T+001] RoadShowcase.001_selected -> status=Road command ready. Right-click near a road or fort. | selected=Blue Vanguard | controlled=Blue Vanguard -9800,0 | vanguard=-9800,0 | north=-9400,700 | south=-9400,-700 | chunks=25 | nodes=135 | roads=9 | cue=Off | camera=-9989,-189 | tick=2.345ms
- [T+006] RoadShowcase.002_command_accepted -> status=Grand Road selected Direct corridor with 17 sampled point(s). | selected=Blue Vanguard | controlled=Blue Vanguard -9800,0 | vanguard=-9800,0 | north=-9400,700 | south=-9400,-700 | chunks=25 | nodes=187 | roads=28 | cue=On | camera=-566,-566 | tick=4.814ms
- [T+069] RoadShowcase.003_column_advancing -> status=Grand Road selected Direct corridor with 17 sampled point(s). | selected=Blue Vanguard | controlled=Blue Vanguard -7336,0 | vanguard=-7336,0 | north=-9400,700 | south=-9400,-700 | chunks=25 | nodes=187 | roads=24 | cue=Off | camera=-566,-566 | tick=1.192ms
- [T+071] RoadShowcase.004_chunk_shifted -> status=Grand Road selected Direct corridor with 17 sampled point(s). | selected=Blue Vanguard | controlled=Blue Vanguard -7256,0 | vanguard=-7256,0 | north=-9400,700 | south=-9400,-700 | chunks=25 | nodes=95 | roads=19 | cue=Off | camera=18000,0 | tick=0.127ms

## Outcome
- success: yes
- verdict: Road showcase passes: selection, road command feedback, spline rendering, movement, and chunk-window migration all behaved as designed.
- reason: selected=`Blue Vanguard` controlled=`Blue Vanguard` status=`Grand Road selected Direct corridor with 17 sampled point(s).` controlled actor `-9800,0` -> `-7336,0` chunk signature `-12884901888,-8589934596,-8589934595,-8589934594,-8589934593,-8589934592,-4294967300,-4294967299,-4294967298,-4294967297,-4294967296,-4,-3,-2,-1,0,4294967292,4294967293,4294967294,4294967295,4294967296,8589934588,8589934589,8589934590,8589934591` -> `-8589934592,-8589934591,-8589934590,-8589934589,-8589934588,-4294967296,-4294967295,-4294967294,-4294967293,-4294967292,0,1,2,3,4,4294967296,4294967297,4294967298,4294967299,4294967300,8589934592,8589934593,8589934594,8589934595,8589934596` cue=visible.

## Summary Stats
- screenshot captures: `5`
- median headless tick: `6.439ms`
- max headless tick: `53.874ms`
- normalized signature: `road_network_showcase_command_and_chunking|selected:Blue Vanguard|controlled:Blue Vanguard|command:0,0|status:Grand Road selected Direct corridor with 17 sampled point(s).|blue:-9800->-7336|chunks:-12884901888,-8589934596,-8589934595,-8589934594,-8589934593,-8589934592,-4294967300,-4294967299,-4294967298,-4294967297,-4294967296,-4,-3,-2,-1,0,4294967292,4294967293,4294967294,4294967295,4294967296,8589934588,8589934589,8589934590,8589934591->-8589934592,-8589934591,-8589934590,-8589934589,-8589934588,-4294967296,-4294967295,-4294967294,-4294967293,-4294967292,0,1,2,3,4,4294967296,4294967297,4294967298,4294967299,4294967300,8589934592,8589934593,8589934594,8589934595,8589934596|roads:28|cue:1`
- reusable wiring: `launcher.runtime.json`, `PlayerInputHandler`, `EntityClickSelectSystem`, `InputOrderMappingSystem`, `AutoPathService`, `RoadSplineBuffer`, `LoadedChunksSource`
