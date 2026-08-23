# Effect History Showcase Design

## One-line promise and audience

In one small scene, a new user can submit the same delayed effect against a live identity, a last-known value, a point, or a cell, then see exactly why the result executed or was rejected after the target changed.

## Main loop

This is a data and pipeline showcase. Its dynamic axis is the combination of target identity, viewer knowledge, execution delay, and entity lifetime. The player changes one of those inputs and watches the formal resolution result and immutable execution record change.

1. The player chooses a target policy with `1` Live, `2` LastKnown, `3` Point, or `4` Cell.
2. The player submits a delayed effect with `Enter`; the link between the cyan source and amber target becomes the pending effect.
3. Before the delay expires, the player can change the delay with `D`/`F`, change the knowledge TTL with `T`/`G`, expire knowledge with `H`, remove the target with `R`, or create a replacement identity with `U`.
4. When the execution tick arrives, the panel shows the real resolver result and appends an execution record containing the original identity, policy, delay, TTL, root id, and tick.

The surprise moment is `R` followed by `U`: the numeric id can be reused, but the old generation remains stale and is never silently redirected to the replacement.

## Ablation comparison

The player can submit the same source, target, delay, and TTL twice, changing only the policy:

- `Live` reads the current authoritative identity.
- `LastKnown` reads the viewer's stored value and returns `Stale` after its TTL expires.
- `Point` and `Cell` remain explicit spatial targets and never select a nearby entity.

After `R` and `U`, the `Live` path returns `Stale` for the old identity. There is no hidden fallback to the replacement or to a nearby value.

## Explanation layer

The right-side panel is mounted through the normal UI surface host. It shows:

- current policy, delay, TTL, simulation tick, and pending execution tick;
- source, target, and replacement identities (`id:world:version`) and whether each is alive;
- knowledge revision and expiry tick;
- the last formal result: `Resolved`, `LastKnown`, `Stale`, `MissingValue`, or `CapacityRejected`;
- a visible rejection message when a lifecycle control cannot be applied;
- the most recent execution records with root id, policy, original target identity, and tick.

The world overlay uses cyan for the source, amber for a live target, violet for a last-known ghost, green for an explicit point/cell, and a highlighted link while an effect is pending. The panel explains these states in plain language and shows failures as results, never as silent no-ops.

## Runtime knobs

| Knob | Runtime control | Range | User question |
|---|---|---:|---|
| Target policy | `1`/`2`/`3`/`4` | four modes | Which value is allowed to resolve the target? |
| Delay | `D`/`F` | 0-30 ticks | How long can the world change before execution? |
| Knowledge TTL | `T`/`G` | 1-60 ticks | How long can the viewer rely on an observation? |
| Lifecycle | `H`, `R`, `U` | keep/expire/remove/reuse | What does the identity contract do when the target changes? |

## Scene structure

### Main scene

A small map contains a cyan source at the left, an amber target at the right, and a persistent overlay panel. The first screen says: “Choose a policy, press Enter, then try H/R/U before the line completes.”

### Sub-scenarios reachable from the main scene

1. Knowledge: submit LastKnown, press `H`, and watch the TTL become stale.
2. Target reference: compare Live, LastKnown, Point, and Cell without changing the map.
3. Lifecycle: press `R`, then `U`; the old identity remains visible in history while the replacement is a different generation.
4. History: submit several effects and read the bounded execution list without querying the current world.

## Portal assets and source of truth

- Design: this file.
- Player UAT: `gitbook/entity/effect-history-showcase-uat.md`.
- Runtime input and map assets: `mods/showcases/effect_history/EffectHistoryShowcaseMod/assets/`.
- Launcher source: `launcher.config.json`, `launcher.presets.json`.
- Registry: `showcase.registry.json`.
- Runtime evidence: `artifacts/acceptance/effect-history/`.

Screenshots must be captured from the running showcase at Live, LastKnown, and Stale states. The panel reads live runtime state; no static mirror of the result is used.

## Reverse API audit

| Required capability | Owner | This delivery |
|---|---|---|
| Generation-safe entity identity | Core Entity History | yes |
| Destroy-time snapshot capture | Core lifecycle contract, wired by showcase reader | yes |
| TTL and revision-aware knowledge value | Core Knowledge | yes |
| Live, LastKnown, Point, and Cell resolution | Core effect target contract | yes |
| Bounded immutable execution record | Core effect history | yes |
| Runtime input, world markers, and readable HUD | Showcase Mod | yes |
| Multi-viewer replication | Networking work | later; not required for this single-viewer showcase |

## Delivery boundary and completion criteria

The real entry is `effect_history_raylib`, backed by `EffectHistoryShowcaseMod` and map `effect_history_showcase`. The showcase is considered playable only when the build, targeted tests, UAT, clean launcher start, Agent Bridge health checks, player input, UI state, and screenshots all agree on the same Mod and map. A successful Core test or a static document alone is not completion.
