# Capability Standard Showcases

This page is the SSOT for production-grade capability acceptance showcase roots in core. Validation, regression launches, and adapter alignment should prefer these root mods instead of legacy business showcase names.

## Acceptance Root Mods

| Scenario | Binding | Root Mod | Acceptance Focus |
|----------|---------|----------|------------------|
| Static Performer Crowd | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K static performers, HUD bars, HUD text, GAS effect state changes |
| Large World Mass Navigation | `capability_standard_mass_nav_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agents, large-world residency, performers, HUD bar/text, effect/minimap changes |
| Total War Like | `capability_standard_total_war_like` | `mods/showcases/capability_standard/CapabilityStandardTotalWarLikeMod` | Formation command, mass movement, selection, path preview, large battle presentation |
| Participant Views | `capability_standard_participant_views` | `mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod` | Map-owned teams/players, local player binding, player/team view projection through formal selection |
| RTS Red Alert Like Production | `rts_redalert_like` | `mods/showcases/production_redalert_like/RedAlertLikeShowcaseMod` | Red Alert-style direct construction, MCV deployment, training queue, progression-gated armor, diplomacy-gated trade |
| RTS StarCraft Like Production | `rts_starcraft_like` | `mods/showcases/production_starcraft_like/StarCraftLikeShowcaseMod` | Terran worker build, Protoss Warp progression, Zerg morph production, three-faction participant views, diplomacy-gated resource trade |
| RTS Empire Like Production | `rts_empire_like` | `mods/showcases/production_empire_like/EmpireLikeShowcaseMod` | Villager construction, building training, Age II/III progression chain, tribute trade and alliance state |
| RTS FourX Like Production | `rts_fourx_like` | `mods/showcases/production_fourx_like/FourXLikeShowcaseMod` | City queue production, three-empire diplomacy matrix, offer/accept trade state, trade pact/war/embargo flags |

Standard launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_static_performer_30k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_mass_nav_large_world_10k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_total_war_like' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_participant_views' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$rts_redalert_like' --adapter web
.\scripts\run-mod-launcher.cmd cli launch '$rts_starcraft_like' --adapter web
.\scripts\run-mod-launcher.cmd cli launch '$rts_empire_like' --adapter web
.\scripts\run-mod-launcher.cmd cli launch '$rts_fourx_like' --adapter web
```

Preset launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_static_performer_30k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_mass_nav_large_world_10k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_total_war_like_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_participant_views_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:rts_redalert_like_web'
.\scripts\run-mod-launcher.cmd cli launch 'preset:rts_starcraft_like_web'
.\scripts\run-mod-launcher.cmd cli launch 'preset:rts_empire_like_web'
.\scripts\run-mod-launcher.cmd cli launch 'preset:rts_fourx_like_web'
```

## Dependency Path

- Root mods own scenario entry, productized config, and minimal scene glue.
- Reusable logic stays in capability mods, for example `MassNavigationMod`, `ParticipantViewCapabilityMod`, `RtsProductionCapabilityMod`, and `RtsHudWebMod`.
- Standard root mod dependency closure must not include historical showcase entry mods such as `PerformerBlacksmithShowcaseMod`, `PerformerBlacksmithScatterHudTextBenchmarkEntryMod`, or `MassNavigationTotalWarEntryMod`.
- Historical showcase mods may remain local debugging material, but they are not adapter or core-mainline acceptance SSOTs.

The RTS production roots share this required dependency closure:

```text
LudotsCoreMod -> CoreInputMod -> ParticipantViewCapabilityMod -> RtsProductionCapabilityMod -> RtsHudWebMod -> <root production showcase mod>
```

`RtsProductionCapabilityMod` owns the production queue, `Owns` edge creation through `OwnershipResolver`, faction collections, progression completion, relationship-gated Exchange execution, and the mod-layer offer/accept state machine. `RtsHudWebMod` owns the retained UI surface via `UiSurfaceLeaseService` and renders the Web HUD through the shared C# `ReactivePage` runtime.

## Adapter Responsibilities

Raylib, Unity, UE5, and other adapters should align against launcher plans for these root mods. Platform work belongs in adapter config, asset binding, host asset resolvers, and platform rendering paths; it must not write private business-project glue back into core.

Adapter authors should verify:

- launcher bindings and presets resolve to the same ordered mod IDs;
- `game.json`, `config_catalog.json`, map, presentation, GAS, input, and camera configs enter runtime through ConfigPipeline;
- HUD bars, HUD text, minimap, selection, and path preview use formal platform rendering paths;
- asset references resolve through `ModId:assets/...`;
- adapters do not hardcode private paths or business names for these showcases.
