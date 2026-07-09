# Capability Standard Showcases

This page is the SSOT for production-grade capability acceptance showcase roots in core. Validation, regression launches, and adapter alignment should prefer these root mods instead of legacy business showcase names.

## Acceptance Root Mods

| Scenario | Binding | Root Mod | Acceptance Focus |
|----------|---------|----------|------------------|
| Static Performer Crowd | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K static performers, HUD bars, HUD text, GAS effect state changes |
| Large World Mass Navigation | `capability_standard_mass_navigation_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agents, large-world residency, performers, HUD bar/text, effect/minimap changes |
| Formation Capability Showcase | `formation_capability_showcase` | `mods/showcases/formation_capability/FormationCapabilityShowcaseMod` | Formation command, mass movement, selection, path preview, large battle presentation |
| Participant Views | `capability_standard_participant_views` | `mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod` | Map-owned teams/players, local player binding, player/team view projection through formal selection |
| Transport Network | `capability_standard_transport_network` | `mods/showcases/capability_standard/CapabilityStandardTransportNetworkMod` | TransportNetwork authoring, deterministic NodeGraph bake, water-ready tags/capacity, SurfaceSpline ribbon derivation |
| Physics2D | `capability_standard_physics2d` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DMod` | Pure Physics2D startup, static polygon wall, restitution bounce, ForceInput knockback, damping field, kinematic rotating door, friction tangent impulse, radial impulse symmetry |
| Physics2D Stress | `capability_standard_physics2d_stress` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DStressMod` | Large-N Physics2D throughput budget and pipeline-level steady-state allocation evidence |
| Physics2D Tuning | `capability_standard_physics2d_showcase` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DShowcaseMod` | 15Hz Physics2D, 30K dynamic entities, 100K static entities, broadphase strategy, static obstacle templates, polygon authoring |
| TimeFlow | `capability_standard_time_flow_showcase` | `mods/showcases/capability_standard/CapabilityStandardTimeFlowShowcaseMod` | TimeFlow pause/scale token stacks: settings pause, menu pause, skill indicator pause, nested system guide pause, scale layering, with MassNavigation, Physics2D, and GAS clock probes and no Formation/action coupling |

Standard launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_static_performer_30k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_mass_navigation_large_world_10k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$formation_capability_showcase' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_participant_views' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_transport_network' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d_stress' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d_showcase' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_time_flow_showcase' --adapter raylib
```

Preset launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_static_performer_30k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_mass_navigation_large_world_10k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:formation_capability_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_participant_views_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_transport_network_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_stress_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_time_flow_showcase_raylib'
```

## Dependency Path

- Root mods own scenario entry, productized config, and minimal scene glue.
- Reusable logic stays in capability mods, for example `MassNavigationMod`, `ParticipantViewCapabilityMod`, and shared Physics2D runtime modules.
- Standard root mod dependency closure must not include historical showcase entry mods such as `PerformerBlacksmithShowcaseMod`, `PerformerBlacksmithScatterHudTextBenchmarkEntryMod`, or `Physics2DPlaygroundMod`.
- The Physics2D capability-standard root retires old `Physics2DPlaygroundMod` as formal entry; historical playgrounds are not acceptance SSOTs.
- Historical showcase mods may remain local debugging material, but they are not adapter or core-mainline acceptance SSOTs.

## Adapter Responsibilities

Raylib, Unity, UE5, and other adapters should align against launcher plans for these root mods. Platform work belongs in adapter config, asset binding, host asset resolvers, and platform rendering paths; it must not write private business-project glue back into core.

Adapter authors should verify:

- launcher bindings and presets resolve to the same ordered mod IDs;
- `game.json`, `config_catalog.json`, map, presentation, GAS, input, and camera configs enter runtime through ConfigPipeline;
- Physics2D root keeps `physics2D.enabled=true`, with runtime bodies spawned through `RuntimeEntitySpawnQueue`;
- HUD bars, HUD text, minimap, selection, and path preview use formal platform rendering paths;
- asset references resolve through `ModId:assets/...`;
- adapters do not hardcode private paths or business names for these showcases.
