# Scenario Card: cross-load-context-save-system

## Player Promise
- A player can name an actor, save the mission, reload the slot, and continue from the same visible state even when the host reloads Ludots assemblies.

## Showcase Beat
1. Enter the core save showcase map.
2. Create `HAN Save Pilot` at the northern gate and set the mission objective.
3. Save into a manual slot.
4. Mutate the target world to prove load replaces unsaved state.
5. Load the slot and continue simulation.

## Pass Signals
- `HAN Save Pilot` keeps the same name and position after load.
- The mission objective returns to the saved value.
- Unsaved target-world actors disappear after load.
- The post-load continuation trace matches the original mission.

## Evidence
- saved: `SaveShowcaseTrace { Stage = save-point, MapId = entry, ActorName = HAN Save Pilot, ActorPosition = (3200cm, 6400cm), MissionObjective = Hold the northern gate, GameSessionTick = 2, FixedFrame = 2 }`
- loaded: `SaveShowcaseTrace { Stage = loaded, MapId = entry, ActorName = HAN Save Pilot, ActorPosition = (3200cm, 6400cm), MissionObjective = Hold the northern gate, GameSessionTick = 2, FixedFrame = 2 }`
- `artifacts/acceptance/save-system-cross-load-context/trace.jsonl`
- `artifacts/acceptance/save-system-cross-load-context/manual-uat.md`
