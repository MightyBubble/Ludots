# 关系系统：市场案例抽象与 Ludots 落地参考

本篇是 `docs/reference/` 下的事实型参考资料，服务于三类目标：

*   从主流商业游戏提炼“关系系统”的共性抽象，避免闭门造车。
*   对齐 Ludots 当前已经落地的通用关系基建、回调点、配置结构与 showcase 入口。
*   给后续开发者一套可复用的实现与验收口径，而不是只看单个 showcase 的写死逻辑。

## 1 市场案例抽象

### 1.1 CRPG：态度、忠诚、派系声望

代表案例：

*   Baldur's Gate 3 `Approval`
    *   参考：<https://bg3.wiki/wiki/Approval>
*   Divinity: Original Sin 2 `Attitude`
    *   参考：<https://divinitywiki.com/index.php/Attitude>
*   Pillars of Eternity `Reputation` / companion relationship
    *   参考：<https://pillarsofeternity.fandom.com/wiki/Reputation>
    *   参考：<https://pillarsofeternity.fandom.com/wiki/Companion_relationship>

抽象结论：

*   需要支持有向边 `A -> B`。
*   需要同时支持连续数值与离散档位。
*   需要支持“跨阈值触发事件/能力/敌友变化”。

### 1.2 JRPG：羁绊、支援等级、社群 Rank

代表案例：

*   Fire Emblem `Support`
    *   参考：<https://fireemblemwiki.org/wiki/Support>
*   Persona 5 `Confidant`
    *   参考：<https://megamitensei.fandom.com/wiki/Confidant>
*   Xenoblade Chronicles `Affinity`
    *   参考：<https://xenoblade.fandom.com/wiki/Affinity_(XC1)>

抽象结论：

*   关系边不能只存单值，还要支持 rank/band。
*   协同动作、剧情结算、赠礼等都需要进入同一套变更入口。
*   RankUp 必须能挂收益回调，而不是把收益逻辑写死在关系系统里。

### 1.3 自走棋：羁绊、联盟、Trait Tier

代表案例：

*   Teamfight Tactics traits
    *   参考：<https://en.wikipedia.org/wiki/Teamfight_Tactics>
*   Dota Auto Chess race/class
    *   参考：<https://en.wikipedia.org/wiki/Dota_Auto_Chess>
*   Dota Underlords alliances
    *   参考：<https://attackofthefanboy.com/guides/dota-underlords-how-to-use-alliances/>

抽象结论：

*   关系系统不能只做“人物对人物”，还必须支持“队伍/编队 -> trait set”的集合关系。
*   计数、tier、被动加成应该由通用处理器驱动，再通过 GAS 发放收益。

### 1.4 三国英雄题材：义理、信赖、敌对、关系面

代表案例：

*   Formation Capability: Three Kingdoms `Guanxi`
    *   参考：<https://en.wikipedia.org/wiki/Total_War:_Three_Kingdoms>
    *   参考：<https://www.pcgamesn.com/formation-capability-three-kingdoms/formation-capability-three-kingdoms-guanxi>
*   Romance of the Three Kingdoms 8 Remake relationship / trust
    *   参考：<https://www.koeitecmoamerica.com/manual/rtk8-remake/en/3500.html>

抽象结论：

*   需要支持 loyalty / trust / hostility 这类多维关系指标。
*   需要支持“关系面变化”与“忠诚危机”这类可被 Trigger 继续扩展的回调点。

## 2 Ludots 当前通用抽象

本次落地没有新造一套平行运行时，而是把关系能力拆成两条可复用主线：

*   `Edge Relationship`
    *   面向 `From -> To` 的有向关系边。
    *   适合 Loyalty、Support、Threat、Approval、Trust。
*   `Set / Team Relationship`
    *   面向队伍或编队的集合计数与 tier。
    *   适合 trait synergy、alliance、roster bonus。

关键设计约束：

*   `Threat` 被视为关系指标，不再单独起一套 aggro 子系统。
*   回调、协同、收益全部通过既有 Trigger / GAS / Team 基建完成。
*   showcase 只提供题材化内容，不复制 Core 运行时。

## 3 已落地的 Core 基建

### 3.1 运行时与注册表

当前关系基础设施位于 `src/Core/Gameplay/Relationships/`：

*   `src/Core/Gameplay/Relationships/RelationshipRuntime.cs`
*   `src/Core/Gameplay/Relationships/RelationshipCatalogRuntime.cs`
*   `src/Core/Gameplay/Relationships/RelationshipChangeBuffer.cs`
*   `src/Core/Gameplay/Relationships/RelationshipMetricRegistry.cs`
*   `src/Core/Gameplay/Relationships/RelationshipFlagRegistry.cs`
*   `src/Core/Gameplay/Relationships/RelationshipBandRegistry.cs`
*   `src/Core/Gameplay/Relationships/RelationshipReasonRegistry.cs`
*   `src/Core/Gameplay/Relationships/RelationshipTeamBootstrapper.cs`

### 3.2 配置管线

关系配置通过既有 ConfigPipeline 合并，不走平行 loader：

*   `src/Core/Gameplay/Relationships/Config/RelationshipCatalogConfig.cs`
*   `src/Core/Gameplay/Relationships/Config/RelationshipCatalogPipelineLoader.cs`

相关治理与合并规范：

*   `docs/architecture/config_pipeline.md`
*   `docs/reference/config_data_merge_best_practices.md`

### 3.3 通用处理器与回调点

本次新增的通用处理器：

*   `src/Core/Gameplay/Relationships/RelationshipCallbackProcessor.cs`
*   `src/Core/Gameplay/Relationships/RelationshipSynergyProcessor.cs`

它们负责：

*   监听关系指标变化与跨阈值事件。
*   把关系变化转成 Trigger 上下文与 GAS 请求。
*   把 team synergy 的 tier 激活落到 team meta-entity。

### 3.4 Engine 接线

引擎接线位于：

*   `src/Core/Engine/GameEngine.cs`
*   `src/Core/Scripting/CoreServiceKeys.cs`

这部分负责把关系运行时、注册表、事件上下文键注入统一服务容器，供系统、Trigger 与 showcase mod 共用。

## 4 当前配置结构与回调契约

当前 showcase 使用的关系目录为：

*   `mods/showcases/relationship/RelationshipShowcaseMod/assets/Relationships/catalog.json`

本次验证过的通用配置能力包括：

*   `Metrics`
    *   `Loyalty`
    *   `Support`
    *   `Threat`
*   `Callbacks`
    *   跨阈值后触发 trusted / oath / threat focus 相关逻辑
*   `Synergies`
    *   以 team meta-entity 为宿主激活蜀军羁绊 tier

与 Trigger / GAS / Team 的接点：

*   Trigger
    *   `src/Core/Scripting/TriggerManager.cs`
*   GAS
    *   `src/Core/Gameplay/GAS/EffectRequestQueue.cs`
    *   `src/Core/Gameplay/GAS/TagOps.cs`
*   Team
    *   `src/Core/Gameplay/Teams/TeamEntityLookup.cs`

结论：

*   关系系统本身只维护关系状态与变更。
*   收益、标记、表现、后续事件链全部走既有基础设施。

## 5 Showcase 题材化落地

showcase mod 位于：

*   `mods/showcases/relationship/RelationshipShowcaseMod/`

题材选型：

*   桃园三兄弟对黄巾军

原因：

*   能同时覆盖 loyalty / oath / threat / synergy。
*   能把“三国英雄题材”的关系幻想和自走棋式队伍羁绊放进同一套通用运行时。

关键文件：

*   入口
    *   `mods/showcases/relationship/RelationshipShowcaseMod/RelationshipShowcaseModEntry.cs`
*   场景状态
    *   `mods/showcases/relationship/RelationshipShowcaseMod/Runtime/RelationshipShowcaseScenarioState.cs`
*   安装触发器
    *   `mods/showcases/relationship/RelationshipShowcaseMod/Triggers/InstallRelationshipShowcaseOnGameStartTrigger.cs`
*   模拟系统
    *   `mods/showcases/relationship/RelationshipShowcaseMod/Systems/RelationshipShowcaseSimulationSystem.cs`
*   表现系统
    *   `mods/showcases/relationship/RelationshipShowcaseMod/Systems/RelationshipShowcasePresentationSystem.cs`

关键输入：

*   `Tab`
    *   切换当前英雄
*   `1`
    *   `Benevolence Doctrine`
*   `2`
    *   `Oath Drill`
*   `3`
    *   `Taunt`
*   `4`
    *   `Rally Banner`

## 6 题材映射是否覆盖四类目标

本次 showcase 与市场抽象的映射关系如下：

*   CRPG
    *   `Loyalty` 跨阈值触发 trusted callback。
*   JRPG
    *   `Support` 跨阈值触发 oath bond 解锁与移动增益。
*   自走棋
    *   蜀军 team synergy 在 roster/team 层激活 tier，并通过 GAS 转成 shield 收益。
*   三国英雄题材
    *   桃园结义题材把 loyalty、support、enemy threat 全部串到同一战斗流程。

因此这套抽象不是“为了三国写一个专用系统”，而是用三国题材承载了四类机制的统一验证。

## 7 验收证据

### 7.1 可玩 acceptance

测试位于：

*   `src/Tests/GasTests/Production/RelationshipShowcasePlayableAcceptanceTests.cs`
*   `src/Tests/GasTests/Production/ProductionAllModsValidationTests.cs`

已验证行为：

*   提前按 `4` 会走 guard branch，不能白拿 buff。
*   `Doctrine` 把 `Loyalty` 推到 trusted 阈值并激活 team synergy。
*   `Oath Drill` 把 `Support` 推到 oath 阈值并通过 GAS 发放移动收益。
*   `Tab` 切换当前英雄，说明 showcase 走的是权威输入链。
*   `Taunt` 把 `Threat` 推高并让敌方 focus 锁定关羽。
*   `Rally Banner` 把已解锁的关系状态转成共享 shield 收益。

### 7.2 产物路径

产物位于 `artifacts/acceptance/relationship-showcase/`：

*   战报
    *   `artifacts/acceptance/relationship-showcase/battle-report.md`
*   Trace
    *   `artifacts/acceptance/relationship-showcase/trace.jsonl`
*   路径图
    *   `artifacts/acceptance/relationship-showcase/path.mmd`
*   截图
    *   `artifacts/acceptance/relationship-showcase/screens/01_doctrine_trust_synergy.png`
    *   `artifacts/acceptance/relationship-showcase/screens/02_rally_banner.png`
    *   `artifacts/acceptance/relationship-showcase/screens/timeline.png`

## 8 已知技术债务

当前桌面 `raylib` 启动链存在一个跨层缺口：

*   关系 showcase 的 live launch 会在宿主启动阶段命中 `Arch` 程序集装载失败。
*   该问题不是关系系统逻辑错误，而是 App / Adapter / Mod loader 边界上的宿主问题。

债务报告见：

*   `artifacts/techdebt/2026-03-23-raylib-relationship-showcase-launch.md`

当前 fuse 决策：

*   不做静默 fallback。
*   维持 headless playable acceptance + PNG evidence 作为本次 feature 验收闭环。
*   桌面 live launch 问题单独作为跨层债务继续处理。

## 9 相关文档

*   `docs/conventions/04_documentation_governance.md`
*   `docs/architecture/config_pipeline.md`
*   `docs/architecture/trigger_guide.md`
*   `docs/architecture/gas_layered_architecture.md`
*   `docs/reference/config_data_merge_best_practices.md`
*   `docs/reference/cli_runbook.md`
