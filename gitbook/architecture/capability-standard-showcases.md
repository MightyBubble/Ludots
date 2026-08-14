# Capability Standard Showcases

This page is the SSOT for production-grade capability acceptance showcase roots in core. Validation, regression launches, and adapter alignment should prefer these root mods instead of legacy business showcase names.

## Acceptance Root Mods

| Scenario | Binding | Root Mod | Acceptance Focus |
|----------|---------|----------|------------------|
| Static Performer Crowd | `capability_standard_static_performer_30k` | `mods/showcases/capability_standard/CapabilityStandardStaticPerformer30kMod` | 30K static performers, HUD bars, HUD text, GAS effect state changes |
| Large World Mass Navigation | `capability_standard_mass_navigation_large_world_10k` | `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod` | 10K nav agents, large-world residency, performers, HUD bar/text, effect/minimap changes |
| Formation Capability Showcase | `formation_capability_showcase` | `mods/showcases/formation_capability/FormationCapabilityShowcaseMod` | Formation command, mass movement, selection, path preview, large battle presentation |
| Participant Views | `capability_standard_participant_views` | `mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod` | Map-owned teams/players, local player binding, player/team view projection through entity collections |
| Transport Network | `capability_standard_transport_network` | `mods/showcases/capability_standard/CapabilityStandardTransportNetworkMod` | TransportNetwork authoring, deterministic NodeGraph bake, water-ready tags/capacity, SurfaceSpline ribbon derivation |
| Physics2D | `capability_standard_physics2d` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DMod` | Pure Physics2D startup, static polygon wall, restitution bounce, ForceInput knockback, damping field, kinematic rotating door, friction tangent impulse, radial impulse symmetry |
| Physics2D Stress | `capability_standard_physics2d_stress` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DStressMod` | Large-N Physics2D throughput budget and pipeline-level steady-state allocation evidence |
| Physics2D Tuning | `capability_standard_physics2d_showcase` | `mods/showcases/capability_standard/CapabilityStandardPhysics2DShowcaseMod` | 15Hz Physics2D, 30K dynamic entities, 100K static entities, broadphase strategy, static obstacle templates, polygon authoring |
| TimeFlow | `capability_standard_time_flow_showcase` | `mods/showcases/capability_standard/CapabilityStandardTimeFlowShowcaseMod` | TimeFlow pause/scale token stacks: settings pause, menu pause, skill indicator pause, nested system guide pause, scale layering, with MassNavigation, Physics2D, and GAS clock probes and no Formation/action coupling |
| Crowd Physics Arena | `capability_standard_crowd_physics_arena` | `mods/showcases/capability_standard/CapabilityStandardCrowdPhysicsArenaMod` | massnav→kinematic bridge acceptance: kinematic squads push dynamic crates, pressure plate contact events open a door, Q shockwave displacement windows with handback, E boulder spawn with initial velocity, HUD counters |
| Script Flow Sandbox | `capability_standard_script_flow_sandbox` | `mods/showcases/capability_standard/CapabilityStandardScriptFlowSandboxMod` | **原子 L1 Script**：Call/Yield/Halt「喝水直到满」水位条；不含 BT/HFSM/Level |
| Behavior Tree Arena | `capability_standard_behavior_tree_arena` | `mods/showcases/capability_standard/CapabilityStandardBehaviorTreeArenaMod` | **可读剧本**：绿卫兵沿黄线巡逻；红敌人出现就追打（追击线）；消失后归位。灰点带=后台万人思考；思考波 &lt;5ms |
| HFSM Sentry Arena | `capability_standard_hfsm_sentry_arena` | `mods/showcases/capability_standard/CapabilityStandardHfsmSentryArenaMod` | **可读剧本**：门岗线哨兵 Idle→警戒→交战→撤退；入侵者来回走；交战生命周期 Script |
| Level Blueprint Trial | `capability_standard_level_blueprint_trial` | `mods/showcases/capability_standard/CapabilityStandardLevelBlueprintTrialMod` | **可读剧本**：走进触发圈→刷怪→清完开门→阶段色块推进 |
| Ability Graph Sandbox | `capability_standard_ability_graph_sandbox` | `mods/showcases/capability_standard/CapabilityStandardAbilityGraphSandboxMod` | **可读剧本**：巡逻查一圈找范围目标，给命中对象挂状态、加好感，并把状态牌读成面板 token |
| Graph Op 单节点画廊 | `capability_standard_graph_op_{Op}` | `graph_op_entries/CapabilityStandardGraphOp{Op}EntryMod` | **玩家入口**：每个可执行图节点单独一场短剧 + 单独录像。共用宿主 `CapabilityStandardGraphOpsNodeGalleryMod`。启动器条目由 `scripts/generate-graph-op-node-galleries.py` 从 vignette 生成。没有按家族打包的大杂烩房间。 |
| Graph Behavior Integration | `capability_standard_graph_behavior_integration` | `mods/showcases/capability_standard/CapabilityStandardGraphBehaviorIntegrationMod` | **单独短剧**：左巡逻 / 右门岗 / 上触发刷敌，串一条故事（不是四套糊在一起） |
| 残血的分更高 | `capability_standard_graph_score` | `mods/showcases/capability_standard/CapabilityStandardGraphScoreShowcaseMod` | **打分短剧**：选人走 GraphScore，字幕读决策痕迹，自动打残血木桩 |

压力矩阵与 &lt;5ms 思考波报告：`docs/benchmarks/graph-behavior-pressure/`（Showcase 主镜头是剧本，万人在无头测试与灰点带）。

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
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_crowd_physics_arena' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_script_flow_sandbox' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_behavior_tree_arena' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_hfsm_sentry_arena' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_level_blueprint_trial' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_ability_graph_sandbox' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_graph_op_AddFloat' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_graph_behavior_integration' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_graph_score' --adapter raylib
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
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_crowd_physics_arena_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_script_flow_sandbox_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_behavior_tree_arena_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_hfsm_sentry_arena_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_level_blueprint_trial_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_ability_graph_sandbox_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_graph_op_AddFloat_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_graph_behavior_integration_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_graph_score_raylib'
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

## GraphNodeOp 单节点画廊

玩家入口是「一场短剧只讲一个节点」，不是把十几个节点塞进同一场。

- 启动绑定：`capability_standard_graph_op_{Op}`
- 剧本：`CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/{Op}.json`
- 作者图：`assets/GAS/graphs/{Op}.json`（FrontDoor，禁止 C# 演戏填字幕）
- 薄入口：`graph_op_entries/CapabilityStandardGraphOp{Op}EntryMod`（只覆盖 `startupMapId`）
- 录像（Git LFS）：`artifacts/evidence/capability_standard_graph_op_{Op}/play.mp4`
- 画廊海报：`artifacts/evidence/capability_standard_graph_op_{Op}/poster.png`
- 玩家 Wiki：`gitbook/reference/graph-node-op-wiki/{Op}.md`（总览 `README.md`）
- 生成器：`scripts/generate-graph-op-node-galleries.py`（launcher / registry / coverage / 地图 / 薄入口）
- Wiki 生成器：`scripts/generate-graph-op-node-wiki.py`（从 vignette 生成，缺录像失败关闭）
- 门户发布：`scripts/build-site.py` 把上述媒体拷进 `_site/`，随 main 的 GitHub Pages 上线

```gherkin
Feature: 每个图节点单独一场可看懂的短剧

  Scenario: 新玩家点开「两段伤害叠在一起」
    Given 玩家从画廊进入 capability_standard_graph_op_AddFloat
    And 舞台上能看见施法者和木桩，头顶有血条
    When 短剧开始演算
    Then 字幕只讲这一刀怎么把两段伤害叠在一起
    And 木桩血条按总和往下掉
    And 这场录像里看不到其它节点的完整剧情

  Scenario: 覆盖表里的每个可执行节点都能单独进
    Given 覆盖表里有一个可执行图节点
    Then 画廊里有且仅有一场以它为主角的短剧
    And 启动器能单独打开这一场
    And 这一场有自己的录像目录
```
