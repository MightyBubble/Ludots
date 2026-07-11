# MassNavigation Config SSOT Acceptance

Build: `codex/issue-642-massnav`
Map binding: `MapConfig.Metadata.massNavigation.profileId`
Clock: deterministic configuration/build validation
Execution date: 2026-07-11

## Timeline

`[T+001] ConfigPipeline.Resolve(ArrayById + extends) -> capability profile | PASS | map metadata remains the sole profile binding`

`[T+002] StrictJson.Deserialize(runtime + sceneAuthoring) -> typed contract | PASS | 194 JsonRequired declarations; unknown members disallowed`

`[T+003] RuntimePlan.Compile -> immutable world/capacity/cadence/flow/streaming/agent profiles | PASS | Simulation.Config removed`

`[T+004] AgentMetadataSync.Observe(bound agents) -> runtime teams | PASS | no scenario team allow-list or showcase dependency`

`[T+005] MassNavigationSceneOwner.PopulateScene -> presentation/scenario/team relationships | PASS | generic bootstrap does not load TeamManager config`

`[T+006] Production builds -> Core + MassNavigation + Formation + participant views + TimeFlow + RoadNetwork | PASS | zero compile errors in all completed builds`

## Outcome

PASS. Execution configuration, scene authoring, and map binding have one owner each. Missing and unknown JSON members fail through the shared System.Text.Json strict contract; no private schema generator or compatibility reader was introduced.

## Summary Stats

- Config domains: 2 (`runtime`, `sceneAuthoring`)
- Profile binding fields outside map metadata: 0
- Mutable `Simulation.Config` aliases: 0
- Hand-maintained MassNavigation `RequireProperty` lists: 0
- Core bootstrap `TeamManager.LoadConfig` calls: 0
- Required-member declarations scanned: 194
- Production build failures: 0
