## GAS Composition Gate - Self Review

- **Task / Issue**: Make the Night Raid graph showcase playable through the existing input and presentation pipelines.
- **Date**: 2026-08-20
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): A

Conclusion: PASS

Reason: The showcase adds no lifecycle variant. It composes existing authoritative input, attribute storage, presentation feedback, and deferred entity destruction around the existing map and TriggerGraph flow.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Player command capture | existing Core input | `PlayerInputHandler` and authoritative input snapshot |
| Target health reduction | showcase interaction | Existing `AttributeBuffer` current value |
| Defeat cleanup | existing presentation lifecycle | `PresentationEntityLifecycle.RequestDestroy` |
| Wave and phase progression | existing Layer 2 | Night Raid map JSON and TriggerGraph |
| HUD and selection ring | presentation | `ScreenOverlayBuffer` and `PresentationWorldFactPublisher` |

### 3. Reuse list

- Handlers: existing map trigger handlers and `PresentationEntityLifecycle.RequestDestroy`.
- Queues / Systems: Core input snapshot, `CommandSourceAcquisitionSystem`, `TabTargetCycleSystem`, map trigger processing, presentation cleanup.
- Resolvers / Registries: `AttributeRegistry`, entity collection context, client local-seat access.
- Existing presets / graphs: the `night_raid` map and TriggerGraph.

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

No new transaction. Each defeated target delegates destruction to the existing deferred lifecycle boundary.

### 6. Config SSOT

Gameplay progression remains in `mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/Maps/night_raid.json` and `assets/GAS/graphs.json`.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum was added.
- [x] No parallel materialization pipeline was created.
- [x] No placement validation was added to a lifecycle operation.
- [x] No unexplained default fallback was added.

### 8. Next variant test

The next Night Raid variation changes: graph connections or effect steps.
