# Champion Skill Stress Scenario

本文定义 `ChampionSkillSandboxMod` 压力测试场景的目标、复用基建、最小运行卡片与验收产物要求。该场景用于在 Raylib 中稳定压测 GAS 的投射物、搜索、治疗、移动补位与表现链路，而不是重新搭一套独立战斗运行时。

## 1. Scenario Card

### 1.1 Intent

* 玩家目标：在同一张压力地图中同时观察两队单位的前排近战、后排火球 / 激光对射、牧师治疗、镜头跟随与施法模式切换，并通过工具面板动态调节两队人数。
* Gameplay domain：`ChampionSkillSandboxMod` 的 GAS 技能、`OrderQueue`、projectile presentation、Entity Command Panel toolbar 与 Raylib 运行时。

### 1.2 Determinism Inputs

* Seed：固定编队生成顺序，不使用随机散布。
* Map：`champion_skill_stress`
* Clock profile：`FixedFrame`
* Initial entities：地图只负责相机与边界；压力单位通过 `RuntimeEntitySpawnQueue` 维持到目标数量。

### 1.3 Action Script

1. 启动 `ChampionSkillStressEntryMod`，默认进入 `champion_skill_stress`。
2. 工具面板显示三种施法模式、镜头跟随 / 复位，以及 `Team A-`、`Team A+`、`Team B-`、`Team B+` 人数控制。
3. 系统按固定编队维护两队单位：
   * 前排 Warrior 自动近战追击。
   * 中后排 Fire Mage 自动发射带 AOE 的 Fireball projectile。
   * 中后排 Laser Mage 自动发射高速 Laser projectile。
   * 后排 Priest 自动寻找受伤友军治疗。
4. 运行期间持续采集 live counts、projectile counts、queue depth、orders issued 等遥测。

### 1.4 Expected Outcomes

* Primary success condition：两队单位达到工具面板设置数量后持续自动交战，Raylib 中可见明确 projectile / cast / hit / heal 反馈。
* Failure branch condition：队伍数量无法收敛、toolbar 无法调整目标人数、projectile / heal cues 缺失、`OrderQueue` 长期饱和不再出单。
* Key metrics：
  * `desired/live` team counts
  * `projectileCount` / `peakProjectileCount`
  * `queueDepth`
  * `ordersIssued`

### 1.5 Evidence Artifacts

* `artifacts/acceptance/champion-skill-stress/trace.jsonl`
* `artifacts/acceptance/champion-skill-stress/battle-report.md`
* `artifacts/acceptance/champion-skill-stress/path.mmd`

## 2. Reuse Infrastructure

### 2.1 Reuse List

复用基建：

* Registry: `AbilityDefinitionRegistry` / `EffectTemplateRegistry` / `ProjectilePresentationBindingRegistry`
  - 用于注册 stress abilities、AOE fireball、laser projectile、priest heal 及其表现绑定。
* Pipeline: `EffectRequestQueue -> EffectProcessingLoopSystem -> ProjectileRuntimeSystem -> GasPresentationEventBuffer`
  - 数据从 ability exec 流向 effect / projectile / presentation，不新增平行战斗管线。
* Pipeline: `PresentationCommandBuffer -> PresenterRuntimeSystem -> PrimitiveDrawBuffer / WorldHudBatchBuffer`
  - 复用 cast cue、hit cue、combat text、ground overlay、projectile presenter。
* Pipeline: `RuntimeEntitySpawnQueue`
  - 用于按目标人数维持压力单位，不直接绕过模板实例化。
* Pipeline: `OrderQueue + CompositeOrderPlanner`
  - 用于自动下单，并保留“超出施法距离先移动再施法”的统一路径。
* System: `ChampionSkillSandboxPresentationSystem` / `ChampionSkillSandboxVisualFeedback`
  - 扩展现有 sandbox 表现反馈，不重写一套技能命中显示。
* System: `EntityCommandPanelPresentationSystem`
  - 继续作为唯一 `UIRoot` owner，压力工具面板通过 global toolbar 扩展，不额外挂第二个 `UiScene`。
* Mod: `ChampionSkillSandboxMod`
  - 作为战斗母体，继续承载 cast modes、selection / hover markers、camera follow 与 command panel。
* Mod: `CameraAcceptanceHotpathEntryMod`
  - 作为单独 startup entry mod 的模式参考，用于压力地图直达启动。
* Mod: `InteractionShowcaseMod`
  - 复用 stress telemetry、spawn-to-target-count、playable acceptance artifact 的组织方式。

### 2.2 Landing Decision

本场景落在 `ChampionSkillSandboxMod`，原因如下：

* 技能、marker、AbilityForm、toolbar、command panel 已在该 mod 内闭环。
* `EntityCommandPanelMod` 已是现有 UI host，继续扩展 toolbar 比新增独立 scene 更安全。
* 压测地图只服务该 showcase，因此新增 map / runtime / systems 属于 mod 内 feature，不需要抽到 Core。

### 2.3 Known Gaps To Fill

* `ChampionSkillSandboxMod` 目前缺少自动压测队伍生成与持续出单系统。
* 当前 toolbar 只有施法模式与镜头按钮，缺少双队人数控制与 stress telemetry 摘要。
* 当前 sandbox 只有英雄对假人验证，没有双阵营持续作战与数量维持地图。

## 3. Minimal Implementation Plan

### 3.1 Runtime

* 新增 `champion_skill_stress` map 与 `ChampionSkillStressEntryMod`。
* 为 `ChampionSkillSandboxMod` 增加 stress control state、telemetry、spawn system、auto-combat system。
* 保持 `ChampionSkillSandboxRuntime` 负责通用 sandbox 行为，stress-specific 逻辑只在 stress map 生效。

### 3.2 Content

* 新增 Warrior / Fire Mage / Laser Mage / Priest 的 team-specific templates。
* 新增 Fireball AOE、Laser projectile、Priest Heal、Warrior Melee abilities / effects。
* 新增 projectile cues 与 cast / hit / heal presenters，全部走 presenter / projectile binding。

### 3.3 Tooling

* 扩展 `ChampionSkillCastModeToolbarProvider`：
  * 保留 Quick / Indicator / RTS。
  * 保留 Free / Follow / Group / Reset。
  * 在 stress map 追加 `A-`、`A+`、`B-`、`B+`。
  * `Subtitle` 输出 live telemetry 摘要。

### 3.4 Acceptance

* 新增 stress config test，覆盖 map / toolbar / effect / projectile binding。
* 新增 playable acceptance test，验证：
  * live team counts 收敛到目标值
  * peak projectiles 大于 0
  * toolbar 能调整两队目标数量
  * battle-report / trace / path artifacts 落盘

## 4. Related Docs

* `docs/architecture/gas_layered_architecture.md`
* `docs/architecture/entity_command_panel_infrastructure.md`
* `docs/reference/cli_runbook.md`
* `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/ability_form_sets.json`
