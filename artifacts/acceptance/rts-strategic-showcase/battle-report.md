# Scenario Card: rts-strategic-showcase

## Intent
- Player goal: validate classic RTS strategic verbs on top of GAS tags, effects, relation parenting, and form-set slot routing.
- Gameplay domain: Warcraft worker build, C&C place-and-rise, Protoss warp tech, Zerg morph, shared garrison/ungarrison.

## Determinism Inputs
- Seed: fixed-step deterministic simulation at 60 FPS.
- Map: `rts_entry`.
- Clock profile: `FixedFrame`.
- Initial entities: Peasant, Barracks, Guard Tower, Footman, Construction Yard, War Factory, Battle Bunker, Rocket Trooper, Gateway, Probe, Drone.

## Action Script
1. Warcraft peasant builds Lumber Mill and Guard Tower via data-driven slots.
2. Barracks trains Footman, then the trained unit garrisons and ungarrisons.
3. Construction Yard places Power Plant and Refinery; War Factory trains Rhino; bunker cycles Rocket Trooper garrison.
4. Gateway trains Zealot, researches Warp Gate, then warps a second Zealot at a world point.
5. Drone morphs into a Spawning Pool and gets consumed on completion.

## Expected Outcomes
- Primary success condition: every strategic action completes using existing GAS tags, effects, relation parenting, and form-set routing.
- Failure branch condition: builders fail to attach/detach, garrisoned units never release, research never grants `Progression.Rts.WarpGate`, or morphing drones survive completion.
- Key metrics:
  total timeline steps: 11
  average frame time ms: 0.392
  peak frame time ms: 25.187

## Timeline
- [T+001] rts_entry loaded with Warcraft worker build, C&C placement, Protoss gateway tech, and Zerg morph actors ready.
- [T+002] War3 build: Peasant.Build(Lumber Mill) spends 160 minerals / 60 lumber, attaches to the site, and freezes selection while the relation is active.
- [T+003] War3 build completion: Lumber Mill clears Constructing, the worker relation is removed, and the peasant regains interaction.
- [T+004] War3 form-set route: the peasant's R-slot override builds a Guard Tower site without new runtime infrastructure.
- [T+005] War3 training: Barracks queues Footman production with a Training tag clip, then spawns a second Footman outside the building.
- [T+006] Shared garrison: Footman enters the tower as ChildOf(Target), becomes unselectable, then exits on UngarrisonAll without custom attach stacks.
- [T+007] C&C placement: Construction Yard stamps down a Power Plant instantly, then the new building exits its short Constructing state.
- [T+008] C&C form-set route: the same conyard gains a Refinery on slot override, proving building palettes can stay purely data-driven.
- [T+009] C&C unit flow: War Factory trains a Rhino while the bunker reuses the same shared garrison/ungarrison relation behavior as the tower.
- [T+010] Protoss tech path: Gateway first trains a Zealot, then researches Warp Gate, gains Progression.Rts.WarpGate, and swaps slot 0 into a point-target warp-in.
- [T+011] Zerg morph: Drone attaches to the Spawning Pool shell, stays non-interactable during Constructing, then is destroyed when the morph completes.

## Evidence Artifacts
- `artifacts/acceptance/rts-strategic-showcase/trace.jsonl`
- `artifacts/acceptance/rts-strategic-showcase/panel-trace.jsonl`
- `artifacts/acceptance/rts-strategic-showcase/battle-report.md`
- `artifacts/acceptance/rts-strategic-showcase/path.mmd`
- `artifacts/acceptance/rts-strategic-showcase/screens/*.svg`
