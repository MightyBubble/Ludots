# AI Utility Autocast 契约

本页记录通用 Utility AI 与 OpenRA Stance 行为包的正式分层契约。长篇方案与证据可放在 `docs/rfcs/` 和 `docs/reference/`，但实现判断以本页为准。

## 三层模型

| 层 | 回答的问题 | Ludots 归属 |
|----|------------|-------------|
| Intent / Order | 我被命令做什么 | 玩家、AI、脚本统一提交 `Order`，进入 `OrderQueue` / `OrderBufferSystem` |
| Behavior / 机制 | 怎么把命令执行出来 | 行为包、Order runtime、GAS、Navigation、Targeting policy |
| Deliberation / Utility AI | 多个有价值选项此刻选哪个 | AI Core 只产出 intent，不执行 gameplay side effect |

AI Core 只能输出 `DecisionIntent` / Order intent。它不得直接扣血、发布 Effect、生成 Projectile、扣 cost、写 cooldown、写 block tag，也不得绕过 Order 校验。

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

AI action 可以引用 ability id / key 作为执行意图的契约数据，但现阶段不改变 `castAbility` Order 的 slot-index 执行语义。存在 ability 引用时，加载期必须通过 `AbilityDefinitionRegistry` 校验；未知 ability id/key 直接 fail-fast。

## GAS 与 GCD 契约

能力执行仍归 GAS：

- cost、cooldown、activation precondition、block tag、damage、effect、projectile 都由 GAS 负责。
- GCD 表达为共享 cooldown tag，复用 `AbilityCooldown.CooldownTagId` 与 `AbilityActivationBlockTags`。
- AI / 行为包只能读取 readiness / gate 状态并提交 Order，不能写 GAS 执行状态。

## 分层边界

AI Core 只保留跨题材通用词：

- `ActuatorReadiness`
- `AimGate`
- `TargetFilter`
- `DecisionIntent`
- `ExecutionPrecondition`

Stance、AttackMove、Guard、Patrol、AutoTarget profile 属于 behavior pack；炮台、采集器、建造者、蓄力施法等业务名词属于对应 gameplay ability / actuator adapter，不进入 AI Core。

## 代码锚点

- `src/Core/Gameplay/AI/Config/AiConfigLoader.cs`：AI 配置加载与引用 fail-fast。
- `src/Core/Gameplay/GAS/Orders/OrderTypeRegistry.cs`：Order type key/id SSOT。
- `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`：ability id 到执行定义的注册表。
- `src/Core/Gameplay/GAS/Components/AbilityCooldown.cs` 与 `AbilityActivationBlockTags.cs`：GCD 共享 cooldown tag 的 GAS 原语。

## 已实现快照

- `AiConfigLoader` 已将 Utility AI authoring 编译为 SoA runtime arrays：`profiles`、`decision_makers`、`decisions`、`considerations`、`target_filters`、`target_filter_ops`、`inputs`、`normalizations`、`curves`、`tasks`、`stances`、`actuators`。
- Utility AI 配置引用在加载期 fail-fast。未知 order type、ability、graph、tag、atom、input、filter、curve 或本地 runtime 引用都会报错，不 fallback 到 `0`。
- `GameEngine` 在 order type、ability、graph 注册后重建 `AiRuntime`，并将 Utility AI 接入主循环：`InputCollection` 做 think scheduling，`PostMovement` 在 spatial refresh 后、`OrderBufferSystem` 前做 decision/order intent submit，`Cleanup` 做 combat memory expiry。
- Utility AI runtime 只提交 order intent。它可以根据配置提交 `moveTo`、`attackTarget` 或其它 order，但不发布 `EffectRequest`、不扣 mana、不写 cooldown、不绕过 GAS 校验。
- `ActuatorReadiness` 与 `AimGate` 是 AI Core 的通用 gate；它们和 cooldown tag、activation block tag、activation precondition 进入同一 autocast 候选流水线，不把炮台等业务词带入 Core。
- `mods/OpenRaStanceBehaviorMod` 是 OpenRA stance 业务行为包。它拥有 `attackMove`、`assaultMove`、`guard`、`setCombatStance`、`scatter`，并把这些业务 order 转换为已有基础 order intent，例如 `moveTo` 与 `attackTarget`。
- AI Inspector 会打印 Utility AI runtime 表规模，并读取 opt-in `UtilityAiDecisionTrace` 输出候选数、最佳 decision、readiness block、task status、最后提交的 order/ability。
