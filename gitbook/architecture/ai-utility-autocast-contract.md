# AI Utility Autocast 契约

本页记录通用 Utility AI 与 Combat Stance 行为包的正式分层契约。长篇方案与证据可放在 `docs/rfcs/` 和 `docs/reference/`，但实现判断以本页为准。

## 三层模型

| 层 | 回答的问题 | Ludots 归属 |
|----|------------|-------------|
| Intent / Order | 我被命令做什么 | 玩家、AI、脚本统一提交 `Order`，进入 `OrderQueue` / `OrderBufferSystem` |
| Behavior / 机制 | 怎么把命令执行出来 | 行为包、Order runtime、GAS、Navigation、Targeting policy |
| Deliberation / Utility AI | 多个有价值选项此刻选哪个 | AI Core 只产出 intent，不执行 gameplay side effect |

AI Core 只能输出 `DecisionIntent` / Order intent。它不得直接扣血、发布 Effect、生成 Projectile、扣 cost、写执行锁定 tag，也不得绕过 Order 校验。

## 普攻就是 autocast ability

普通攻击不是特殊系统。它是一种带 autocast policy 的 ability：

- 普攻作为显式配置的候选存在，通常优先级最低、前置最宽松、可自动重复。
- attack-move 的接战阶段等价于“普攻这个 autocast 候选赢得动作槽”。
- Idle、HoldFire、ReturnFire 等都必须是显式配置的 stance / decision / order state；缺配置时 fail-fast，不允许平台 fallback。

单个或互不冲突的 autocast 可以由机制层触发；多个 autocast 争抢共享 GCD、mana 或施法槽时，仲裁器就是 Utility 决策入口。便宜优先级表和 utility 打分是同一候选流水线的两种选择函数。

## Order 契约

AI 配置中的 Order 引用必须收敛到 `OrderTypeRegistry`：

- Authoring 优先写 `OrderTypeKey`，加载期解析为 `OrderTypeId`。
- 已编译或测试配置可以写 `OrderTypeId`，但加载期必须确认该 id 已注册。
- 同时写 `OrderTypeKey` 与 `OrderTypeId` 时，两者必须指向同一个注册 order type。
- `OrderTagId` 不是 AI Order 契约字段，加载期必须报错。
- 缺失、未知或非正数 order type 不允许 fallback 到 `0`。

AI action 描述 typed Order intent。`castAbility` 这类订单的技能选择由订单参数、槽位解析和 GAS 执行路径负责，AI 配置不直接引用 ability id / key。

## GAS 与 GCD 契约

能力执行仍归 GAS：

- cost、activation precondition、block tag、damage、effect、projectile 都由 GAS 负责。
- GCD 表达为 GAS duration Effect 授予共享锁定 tag，ability 通过 `AbilityActivationBlockTags` 读取该 tag。
- AI / 行为包只能读取 AI 自己的感知和 gate 状态并提交 Order，不能写 GAS 执行状态。

## 分层边界

AI Core 只保留跨题材通用词：

- `ActuatorReadiness`
- `AimGate`
- `TargetFilter`
- `DecisionIntent`
- `ExecutionPrecondition`

Stance、AttackMove、Guard、Patrol、AutoTarget profile 属于 behavior pack；炮台、采集器、建造者、蓄力施法等业务名词属于对应 gameplay ability / actuator adapter，不进入 AI Core。

## 数据挂载 authoring 契约

AI / stance 运行时组件可以由 `Entities/templates.json` 或 map `components` 数据挂载，但 authoring 必须使用字符串 key，加载期解析成运行时 int：

- `UtilityAiAgent` 写 `{ "profile": "Profile.Basic" }`，由 `AiRuntime.UtilityRuntime.Authoring` 解析 profile index；`profileId` / `ProfileId` 数字字段直接报错。
- `UtilityAiTargetPriority` 写 `{ "bucket": "High" }`，bucket 只能是 `None` / `Low` / `Normal` / `High` / `Critical` 枚举名；`Bucket` 数字字段直接报错。
- `ActuatorReadiness` / `AimGate` 写 `{ "actuator": "Actuator.Primary" }`，由 `AI/actuators.json` 的 `id` 解析 actuator index；`actuatorId` / `ActuatorId` 数字字段直接报错。可选初始字段必须使用 `initialReady01` / `initialBlockReason` / `initialEtaSteps` / `requiresPreparation`，不暴露热路径内部步进字段。
- `AI/actuators.json` 只声明 `{ "id": "Actuator.Primary" }` 这样的命名表。`ReadinessInput` / `AimGateInput` 不属于可消费字段，加载期直接报错。
- `AI/inputs.json` 若使用 `ActuatorReadiness01`，写 `{ "Kind": "ActuatorReadiness01", "Actuator": "Actuator.Primary" }`；`ActuatorId` 数字字段直接报错。
- `CombatStanceState` 属于 `CombatStanceBehaviorMod`，由该 Mod 注册 authoring。数据写 `{ "stance": "ReturnFire" }`，stance 只能是 `HoldFire` / `ReturnFire` / `Defend` / `AttackAnything`；`stanceId` / `Stance` 数字字段直接报错。
- `AI/profiles.json` 的默认 stance 写 `DefaultStance` 字符串 key；`DefaultStanceId` 数字字段直接报错。

`ComponentRegistry.Apply` 的 fail-fast 信息必须带上组件挂载上下文。模板路径通过 `ConfigConflictReport` 的 winner source URI 传入，map / runtime spawn 路径传入 map id、entity instance 或 template id，方便定位未知 profile、stance、bucket、actuator 等引用。

## 代码锚点

- `src/Core/Gameplay/AI/Config/AiConfigLoader.cs`：AI 配置加载与引用 fail-fast。
- `src/Core/Gameplay/GAS/Orders/OrderTypeRegistry.cs`：Order type key/id SSOT。
- `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`：GAS ability 定义注册表。
- `AbilityActivationBlockTags` 与 duration Effect granted tags：共享锁定 tag 的 GAS 原语。

## 已实现快照

- `AiConfigLoader` 已将 Utility AI authoring 编译为 SoA runtime arrays：`profiles`、`decision_makers`、`decisions`、`considerations`、`target_filters`、`target_filter_ops`、`inputs`、`normalizations`、`curves`、`tasks`、`stances`、`actuators`。
- Utility AI 配置引用在加载期 fail-fast。未知 order type、graph、tag、atom、input、filter、curve 或本地 runtime 引用都会报错，不 fallback 到 `0`。
- `GameEngine` 在 order type、ability、graph 注册后重建 `AiRuntime`，并将 Utility AI 接入主循环：`InputCollection` 做 think scheduling，`PostMovement` 在 spatial refresh 后、`OrderBufferSystem` 前做 decision/order intent submit，`Cleanup` 做 combat memory expiry。
- Utility AI runtime 只提交 order intent。它可以根据配置提交 `moveTo`、`attackTarget` 或其它 order，但不发布 `EffectRequest`、不扣 mana、不写执行锁定 tag、不绕过 GAS 校验。
- `ActuatorReadiness` 与 `AimGate` 是 AI Core 的通用 gate；GAS activation block tag 与 activation precondition 留在订单执行路径，不进入 Utility AI 候选流水线。
- `mods/CombatStanceBehaviorMod` 是 Combat stance 业务行为包。它拥有 `attackMove`、`assaultMove`、`guard`、`setCombatStance`、`scatter`，并把这些业务 order 转换为已有基础 order intent，例如 `moveTo` 与 `attackTarget`。
- AI Inspector 会打印 Utility AI runtime 表规模，并读取 opt-in `UtilityAiDecisionTrace` 输出候选数、最佳 decision、task status、最后提交的 order。
