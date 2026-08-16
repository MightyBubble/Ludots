# UX Prototype Acceptance

## Scenario
- Load the prototype battlefield mod with the Ludots UI/runtime stack.
- Validate that the HUD mounts with objective, resource, roster, and global panel surfaces.
- Queue one construction, one production task, and one navmesh bake through the prototype state service.
- Advance the authoritative engine until new content is spawned and the editor task completes.

## Timeline
- [T+001] Prototype map loaded and Ludots-native HUD mounted.
- [T+002] Runtime state queued construction, production, and navmesh bake; simulation completed and spawned new content.

## Outcome
- success: yes
- farms: 2 -> 3
- workers: 2 -> 3
- median tick: 1.985ms
- max tick: 7.530ms
