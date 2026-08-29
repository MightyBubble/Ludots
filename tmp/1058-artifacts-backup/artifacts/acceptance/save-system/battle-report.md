# Scenario Card: save-system

## Intent
- Player goal: prove the Core save system can persist an existing RTS training showcase state, mutate it, reload it, and continue deterministically.
- Gameplay domain: `rts_cnc_training` via LudotsCoreMod, CoreInputMod, EntityCommandPanelMod, RtsDemoMod, and RtsCncTrainingShowcaseMod.

## Action Script
1. Load the existing C&C RTS training showcase.
2. Add an observable save marker and domain state.
3. Save through `WorldSnapshotService` + `SaveSlotStore`.
4. Mutate the live world and domain state.
5. Restore into a fresh engine and compare state plus deterministic continuation trace.

## Expected Outcomes
- `War Factory` and `Armor Display` survive restore from the existing showcase map.
- `Save UAT Marker` returns to the saved position.
- GameSession globals and Core clock continue from the save point.
- Post-restore trace equals the continuous trace.

## Evidence
- save point: `SavePointTrace { Stage = save-point, MapId = rts_cnc_training, GameSessionTick = 3, FixedFrame = 2, WarFactoryAlive = True, ArmorDisplayAlive = True, MarkerPosition = (12345cm, 23456cm), UatStage = saved }`
- restored point: `SavePointTrace { Stage = restored, MapId = rts_cnc_training, GameSessionTick = 3, FixedFrame = 2, WarFactoryAlive = True, ArmorDisplayAlive = True, MarkerPosition = (12345cm, 23456cm), UatStage = saved }`
- `artifacts/acceptance/save-system/trace.jsonl`
- `artifacts/acceptance/save-system/manual-uat.md`
