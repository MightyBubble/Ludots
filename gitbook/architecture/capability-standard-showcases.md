# Capability Standard Showcases

This page is the SSOT for production-grade capability acceptance showcase roots in core. Validation, regression launches, and adapter alignment should prefer these root mods instead of legacy business showcase names.

## Acceptance Root Mods

| Scenario | Binding | Root Mod | Acceptance Focus |
|----------|---------|----------|------------------|
| Static Performer Crowd | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K static performers, HUD bars, HUD text, GAS effect state changes |
| Large World Mass Navigation | `capability_standard_mass_nav_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agents, large-world residency, performers, HUD bar/text, effect/minimap changes |
| Total War Like | `capability_standard_total_war_like` | `mods/showcases/capability_standard/CapabilityStandardTotalWarLikeMod` | Formation command, mass movement, selection, path preview, large battle presentation |
| Participant Views | `capability_standard_participant_views` | `mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod` | Map-owned teams/players, local player binding, player/team view projection through formal selection |

Standard launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_static_performer_30k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_mass_nav_large_world_10k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_total_war_like' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_participant_views' --adapter raylib
```

Preset launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_static_performer_30k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_mass_nav_large_world_10k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_total_war_like_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_participant_views_raylib'
```

## Dependency Path

- Root mods own scenario entry, productized config, and minimal scene glue.
- Reusable logic stays in capability mods, for example `MassNavigationMod` and `ParticipantViewCapabilityMod`.
- Standard root mod dependency closure must not include historical showcase entry mods such as `PerformerBlacksmithShowcaseMod`, `PerformerBlacksmithScatterHudTextBenchmarkEntryMod`, or `MassNavigationTotalWarEntryMod`.
- Historical showcase mods may remain local debugging material, but they are not adapter or core-mainline acceptance SSOTs.

## Adapter Responsibilities

Raylib, Unity, UE5, and other adapters should align against launcher plans for these root mods. Platform work belongs in adapter config, asset binding, host asset resolvers, and platform rendering paths; it must not write private business-project glue back into core.

Adapter authors should verify:

- launcher bindings and presets resolve to the same ordered mod IDs;
- `game.json`, `config_catalog.json`, map, presentation, GAS, input, and camera configs enter runtime through ConfigPipeline;
- HUD bars, HUD text, minimap, selection, and path preview use formal platform rendering paths;
- asset references resolve through `ModId:assets/...`;
- adapters do not hardcode private paths or business names for these showcases.
