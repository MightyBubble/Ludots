# Scenario Card: relationship-showcase

## Header
- scenario: `relationship-showcase`
- build: `GameEngine 1.0.0.0`
- execution_timestamp_utc: `2026-04-28T23:01:21.1411884+00:00`
- map: `relationship_showcase`
- clock: `fixed 1/60s`

## Intent
- Player goal: prove one reusable relationship runtime can drive CRPG trust, JRPG support rank, auto-battler synergy tiers, and Three Kingdoms oath fantasy inside a playable Ludots mod.
- Gameplay domain: ECS relationship edges, team meta-entity synergy, GAS effects, Trigger callbacks, authoritative input, ground overlay rings, and one reusable narrative frontend scene.

## Determinism Inputs
- Seed: none
- Map: `relationship_showcase`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `RelationshipShowcaseMod`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` backed by a deterministic keyboard backend.

## Action Script
1. Load the Peach Garden showcase, confirm the relationship panel mounts, and verify ground ring telemetry is non-zero.
2. Trigger the rally guard branch before trust, oath, and synergy are ready.
3. Cast Benevolence Doctrine to unlock CRPG-style trust thresholds and auto-battler team synergy.
4. Run Oath Drill to push JRPG-style support rank over the unlock threshold.
5. Rotate selection, taunt into enemy focus, wait for threat-driven strikes, then rally to cash out the unlocked relationship state as GAS buffs.

## Expected Outcomes
- Primary success condition: relationship callbacks, team synergy, Trigger events, and GAS effects all resolve on the production runtime path.
- Failure branch condition: pressing rally before unlocks exist must deny cleanly without granting buffs.
- Key metrics: loyalty, support, threat, synergy state, selected/focused hero, shield/health deltas, UI surface text, and ground ring telemetry.

## Evidence Artifacts
- `artifacts/acceptance/relationship-showcase/trace.jsonl`
- `artifacts/acceptance/relationship-showcase/battle-report.md`
- `artifacts/acceptance/relationship-showcase/path.mmd`
- `artifacts/acceptance/relationship-showcase/5w1h.md`
- `artifacts/acceptance/relationship-showcase/screens/001_map_loaded.png`
- `artifacts/acceptance/relationship-showcase/screens/002_guard_branch_rally_denied.png`
- `artifacts/acceptance/relationship-showcase/screens/003_doctrine_trust_synergy.png`
- `artifacts/acceptance/relationship-showcase/screens/004_oath_rank_up.png`
- `artifacts/acceptance/relationship-showcase/screens/005_selection_rotated.png`
- `artifacts/acceptance/relationship-showcase/screens/006_threat_focus_and_enemy_strike.png`
- `artifacts/acceptance/relationship-showcase/screens/007_rally_banner.png`
- `artifacts/acceptance/relationship-showcase/screens/timeline.png`

## Timeline
- [T+001] relationship_showcase booted with Peach Garden panel text mounted and GroundOverlayBuffer ring telemetry already live.
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
- median headless tick: `0.410ms`
- max headless tick: `8.898ms`
- final loyalty: `Liu->Guan 65`, `Liu->Zhang 65`
- final support: `Guan->Zhang 60`
- final ground rings: `53`
- final ui excerpt: `Faction State | Peach Garden Covenant | Selected Hero | Guan Yu | Enemy Focus`
- reusable wiring: `RelationshipRuntime`, `RelationshipChangeBuffer`, `RelationshipCatalogPipelineLoader`, `RelationshipCatalogInstaller`, `RelationshipProcessingSystem`, `RelationshipCallbackProcessor`, `RelationshipSynergyProcessor`, `TriggerManager`, `EffectRequestQueue`, `TeamEntityLookup`
