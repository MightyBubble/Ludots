# Capability Standard Showcases

This page is the SSOT for production-grade capability acceptance showcase roots in core. Validation, regression launches, and adapter alignment should prefer these root mods instead of legacy business showcase names.

## Acceptance Root Mods

| Scenario | Binding | Root Mod | Acceptance Focus |
|----------|---------|----------|------------------|
| Static Performer Crowd | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K static performers, HUD bars, HUD text, GAS effect state changes |
| Large World Mass Navigation | `capability_standard_mass_nav_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agents, large-world residency, performers, HUD bar/text, effect/minimap changes |
| Total War Like | `capability_standard_total_war_like` | `mods/showcases/capability_standard/CapabilityStandardTotalWarLikeMod` | Formation command, mass movement, selection, path preview, large battle presentation |
| Participant Views | `capability_standard_participant_views` | `mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod` | Map-owned teams/players, local player binding, player/team view projection through formal selection |
| Physics2D | `capability_standard_physics2d` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DMod` | Pure Physics2D startup, static polygon wall, restitution bounce, ForceInput knockback, damping field, kinematic rotating door, friction tangent impulse, radial impulse symmetry |
| Knockback2D | `capability_standard_knockback2d` | `mods/showcases/capability_standard/CapabilityStandardKnockback2DMod` | GAS displacement, `MovementSuppressed2D`, no residual locomotion drift during CC, Physics2D position correction |
| Physics2D Stress | `capability_standard_physics2d_stress` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DStressMod` | Large-N Physics2D throughput budget and pipeline-level steady-state allocation evidence |
| Nav Sink 2D | `capability_standard_nav_sink2d` | `mods/showcases/capability_standard/CapabilityStandardNavSink2DMod` | Nav flow sink, `OrderBuffer` bootstrap, Physics2D velocity handoff, manifestation obstacle bridge to Physics2D and Nav obstacle facts |
| Physics2D Playground v2 | `capability_standard_physics2d_playground_v2` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DPlaygroundV2Mod` | Interactive Physics-only and Nav partitions over production Physics2D/Nav systems; retires old `Physics2DPlaygroundMod` as formal entry |

Standard launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_static_performer_30k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_mass_nav_large_world_10k' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_total_war_like' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_participant_views' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_knockback2d' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d_stress' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_nav_sink2d' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_physics2d_playground_v2' --adapter raylib
```

Preset launch commands:

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_static_performer_30k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_mass_nav_large_world_10k_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_total_war_like_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_participant_views_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_knockback2d_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_stress_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_nav_sink2d_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_physics2d_playground_v2_raylib'
```

## Dependency Path

- Root mods own scenario entry, productized config, and minimal scene glue.
- Reusable logic stays in capability mods, for example `MassNavigationMod` and `ParticipantViewCapabilityMod`.
- #361 Physics2D/Nav UAT roots write headless evidence to `artifacts/showcases/<name>/{acceptance.md,keyframes.jsonl}` through focused NUnit acceptance tests.
- Pure Physics2D roots (`capability_standard_physics2d`, `capability_standard_knockback2d`, `capability_standard_physics2d_stress`) keep `navigation2D.enabled=false` and do not author Nav components in templates.
- Nav-integrated roots (`capability_standard_nav_sink2d`, `capability_standard_physics2d_playground_v2`) keep Nav and Physics responsibilities split: Nav outputs desired velocity or obstacle facts; Physics2D owns velocity commit, position integration, and collision correction.
- `MovementSuppressed2D` means the pre-integration movement-authority pass clears locomotion `Velocity2D.Linear` every Physics2D tick. During CC, movement may only come from displacement-authored `Position2D` steps and Physics2D collision response; once the tag is removed, `NavDesiredVelocity2D` may be committed again on the next sync.
- Standard root mod dependency closure must not include historical showcase entry mods such as `PerformerBlacksmithShowcaseMod`, `PerformerBlacksmithScatterHudTextBenchmarkEntryMod`, or `MassNavigationTotalWarEntryMod`.
- Historical showcase mods may remain local debugging material, but they are not adapter or core-mainline acceptance SSOTs.

## Adapter Responsibilities

Raylib, Unity, UE5, and other adapters should align against launcher plans for these root mods. Platform work belongs in adapter config, asset binding, host asset resolvers, and platform rendering paths; it must not write private business-project glue back into core.

Adapter authors should verify:

- launcher bindings and presets resolve to the same ordered mod IDs;
- `game.json`, `config_catalog.json`, map, presentation, GAS, input, and camera configs enter runtime through ConfigPipeline;
- Physics2D root keeps `physics2D.enabled=true` and `navigation2D.enabled=false`, with runtime bodies spawned through `RuntimeEntitySpawnQueue`;
- HUD bars, HUD text, minimap, selection, and path preview use formal platform rendering paths;
- asset references resolve through `ModId:assets/...`;
- adapters do not hardcode private paths or business names for these showcases.
