# Scenario Card: relationship-showcase

## Intent
- Player goal: prove one reusable relationship runtime can drive CRPG trust, JRPG support rank, auto-battler synergy tiers, and Three Kingdoms oath fantasy inside a playable Ludots mod.
- Gameplay domain: ECS relationship edges, team meta-entity synergy, GAS effects, Trigger callbacks, input-driven showcase presentation, and deterministic battle telemetry.

## Determinism Inputs
- Seed: none
- Map: `relationship_showcase`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `RelationshipShowcaseMod`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` backed by a deterministic keyboard backend.

## Action Script
1. Load the Peach Garden showcase and confirm the relationship panel plus world rings render.
2. Trigger the rally guard branch before trust, oath, and synergy are ready.
3. Cast Benevolence Doctrine to unlock CRPG-style trust thresholds and auto-battler team synergy.
4. Run Oath Drill to push JRPG-style support rank over the unlock threshold.
5. Rotate selection, taunt into enemy focus, wait for threat-driven strikes, then rally to cash out the unlocked relationship state as GAS buffs.

## Expected Outcomes
- Primary success condition: relationship callbacks, team synergy, Trigger events, and GAS effects all resolve on the production runtime path.
- Failure branch condition: pressing rally before unlocks exist must deny cleanly without granting buffs.
- Key metrics: loyalty, support, threat, synergy state, selected/focused hero, shield/health deltas, overlay visibility, and recent battle log lines.

## Evidence Artifacts
- `artifacts/acceptance/relationship-showcase/trace.jsonl`
- `artifacts/acceptance/relationship-showcase/battle-report.md`
- `artifacts/acceptance/relationship-showcase/path.mmd`
- `artifacts/acceptance/relationship-showcase/screens/01_doctrine_trust_synergy.png`
- `artifacts/acceptance/relationship-showcase/screens/02_rally_banner.png`
- `artifacts/acceptance/relationship-showcase/screens/timeline.png`
- `artifacts/techdebt/2026-03-23-raylib-relationship-showcase-launch.md`

## Timeline
- [T+001] relationship_showcase booted with Peach Garden panel text and world highlight rings visible.
- [T+002] Rally guard branch rejected because trust, oath, or synergy thresholds were still locked.
- [T+003] Liu Bei.Benevolence Doctrine -> Loyalty(Liu->Guan=65, Liu->Zhang=65) | Trusted callbacks fired | Shu synergy online.
- [T+004] Guan Yu + Zhang Fei.Oath Drill -> Support rank crossed to 60/60 and movement buffs landed through GAS.
- [T+005] Player rotated focus with Tab to Guan Yu, proving the showcase is playable through authoritative input.
- [T+006] Guan Yu.Taunt -> Threat(Captain=95, Spearman=90) | enemy focus locked on Guan Yu | HP 180 -> 156.
- [T+007] Guan Yu.Rally Banner converted relationship state into shared GAS buffs | Liu Shield 16->30 | Zhang Shield 22->36.

## Outcome
- success: yes
- verdict: the showcase stayed on the shared Ludots relationship, trigger, and GAS infrastructure from threshold unlocks to enemy focus and final rally conversion.
- reason: final state is selected `Guan Yu`, enemy focus `Guan Yu`, trusted `True`, oath `True`, synergy `True`, support `60`, threat `95`.

## Summary Stats
- snapshots captured: `7`
- median headless tick: `0.099ms`
- max headless tick: `14.239ms`
- final loyalty: `Liu->Guan 65`, `Liu->Zhang 65`
- final support: `Guan->Zhang 60`
- final overlay counts: `screen=1512`, `ground=2`, `rings=2`
- reusable wiring: `RelationshipRuntime`, `RelationshipChangeBuffer`, `RelationshipCatalogPipelineLoader`, `RelationshipCatalogInstaller`, `RelationshipProcessingSystem`, `RelationshipCallbackProcessor`, `RelationshipSynergyProcessor`, `TriggerManager`, `EffectRequestQueue`, `TeamEntityLookup`

## Open Tech Debt
- debt_id: `TD-2026-03-23-raylib-relationship-showcase-launch`
- status: `open`
- note: headless acceptance and PNG evidence are complete, but live raylib launch still hits a host-side `Arch` assembly load failure recorded in `artifacts/techdebt/2026-03-23-raylib-relationship-showcase-launch.md`.
