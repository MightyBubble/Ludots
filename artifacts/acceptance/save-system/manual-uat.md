# Manual UAT: Core Save/Load

## Target

- Showcase: `rts_cnc_training`
- Existing mod chain: `LudotsCoreMod`, `CoreInputMod`, `EntityCommandPanelMod`, `RtsDemoMod`, `RtsCncTrainingShowcaseMod`
- Automated evidence: `dotnet test src\Tests\PersistenceTests\PersistenceTests.csproj --filter SaveSystemUatTests`

## Steps

1. Launch the C&C RTS training showcase with the Raylib launcher preset or an equivalent local launcher path.
2. Confirm the map loads with `War Factory` and `Armor Display` visible.
3. Trigger a manual save at a clean tick boundary.
4. Change the world state visibly: advance simulation, move the camera, or create/modify units through the existing RTS controls.
5. Trigger load for the saved slot.
6. Confirm the map returns to the saved state: entity count, positions, current tick, and visible presentation all match the save point.
7. Continue simulation for several ticks and confirm no invalid entity references, missing presenters, or stale UI rows appear.

## Pass Criteria

- Load rejects incompatible schema/mod/registry state fail-fast.
- Existing save remains readable if a later write is interrupted.
- Autosave slots rotate without deleting manual slots.
- Restore is deterministic: the continuation trace from the restored state equals the original world's continuation trace.