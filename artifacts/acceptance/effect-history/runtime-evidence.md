# Effect History Runtime Evidence

The screenshots in `screens/` were captured from the running `effect_history_showcase` session through Agent Bridge.

| Evidence | Player action | Observable result |
|---|---|---|
| `screens/start.png` | Enter the showcase | Cyan source, amber target, controls, policy and lifecycle state are visible. |
| `screens/stale.png` | Select LastKnown, set TTL to 1, submit, press H | The pending effect completes with `Stale`. |
| `screens/point.png` | Select Point and submit | The green explicit spatial marker remains visible and the record is `Resolved`. |
| `screens/reuse.png` | Select Live, submit, press R then U | Original `7:0:1` is dead, replacement `7:0:2` is alive, and the record is `Stale`. |
| `screens/reload.png` | Fire MapUnloaded then MapLoaded | The new session has fresh identities and an empty execution history. |

The session was `effect_history_showcase` with `LudotsCoreMod`, `CoreInputMod`, `EffectHistoryShowcaseMod`, and `AgentBridgeMod`; `/health` pumpCount increased from 258 to 269 before interaction.
