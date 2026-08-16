# Scenario: rfc0065-show6-control-scheme-wasd

## Header
- build: GasTests / Show6Workflow_ControlSchemeHotSwitchEnablesWasdAxisMoveThroughOrderBuffer
- seed: interaction_showcase_hub deterministic headless run
- execution timestamp UTC: 2026-08-16T17:27:18.6501856+00:00

## Scenario Card
- Player goal: hot-switch from mouse/default command scheme to a WASD movement scheme and hold D.
- Runtime path: `ControlSchemeRuntime.TrySwitch` -> `PlayerInputHandler` -> `InputRuntimeSystem` -> `AuthoritativeInputSnapshotSystem` -> `AxisMoveOrderSystem` -> `OrderQueue` -> `OrderBufferSystem`.
- Primary success condition: the local showcase actor receives a moveTo order offset by the scheme-owned axisMove step distance.
- Evidence boundary: this is headless production-path evidence; it does not claim a captured visible UAT recording.

## Runtime Values
| Field | Value |
|---|---|
| local player actor | Entity = { Id = 8, WorldId = 13, Version = 1 } |
| scheme.default registry id | 1 |
| scheme.wasd_move registry id | 2 |
| axis action | Move |
| step distance cm | 400 |
| order id | 1 |
| start world cm | (1600, 1260) |
| target world cm | (2000, 1260) |
