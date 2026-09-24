# AI Utility Autocast 契约

本页记录通用 Utility AI 与 Combat Stance 行为包的正式分层契约。长篇方案与证据可放在 `docs/rfcs/` 和 `docs/reference/`，但实现判断以本页为准。

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

### Order 生命周期回执

Utility 任务必须按正式 Order 生命周期推进，不能把全局队列接收当成执行成功：

- `SubmitOrder` 只调用会回写 `OrderId` 的 `OrderQueue.SubmitAssigned`，并把该 id 保存到 `UtilityAiState.LastSubmittedOrderId`。Utility 不直接写 `OrderBuffer` 或 GAS 状态。
- 全局队列返回接受时，任务状态是 `Submitted`；实体准入返回 `Pending` 时继续等待，返回 `Activated` / `Queued` 时才进入 `Admitted`。
- 普通 `SubmitOrder` 在 `Admitted` 完成的是“订单已由实体接纳”这一提交任务，状态不得写成 ability execution `Completed`。此时才能提交 decision repeat delay。
- 带 `KeepRunningUntilFinished` 的 decision 在 `Admitted` 后继续等待相同 `OrderId` 的 `OrderTerminalResultBuffer` 回执；只有 `Completed` 才成功，`Failed` / `Cancelled` 保留类型化失败或取消结果，且不提交 repeat delay。
- 实体准入拒绝使用 `OrderSubmitResultSemantics` 转成正式 `OrderFailureReason`，清除正在运行的 decision，让下一次思考可以重选；不得留下假成功、冷却或重复延迟。
- 回执消费者在同一逻辑步的所有 Order 终态生产者之后运行。实体准入回执缺失，或等待终态的订单已经离开实体固定容量 OrderBuffer 却没有终态回执，都是明确运行错误；不猜测结果。
- 消费只读取当前固定容量准入/终态快照和实体自身最多 8 个排队槽位，不保留或扫描无界历史，不在热路径分配内存或改变 ECS 结构。

### Utility task 与 decision 配置

当前 Utility task 只支持一种完整行为：

- `AI/tasks.json` 的 `Kind` 必须显式写 `SubmitOrder`。`Sequence`、`Parallel`、`ParallelComplete` 和任何未知值都在加载期报“尚未支持”；运行时遇到非法枚举也会在提交 Order 前抛错，绝不按 `SubmitOrder` 兜底。
- 每个 decision 的 `Tasks` 必须且只能引用一个 task。加载后保存单一 `TaskIndex`，运行时只执行这一项，不存在“提交第一项、静默忽略剩余项”的合同。
- decision 可写字段为 `id`、`TargetFilter`、`Priority`、`BaseScore`、`Weight`、`MomentumBonus`、`MinDurationSteps`、`DecisionRepeatDelaySteps`、`AbilityKey` / `AbilityId`、`AbilitySlotIndex`、`KeepRunningUntilFinished`、`Considerations`、`Tasks`。未知字段直接拒绝。
- `Autocast`、`OrdinaryAttack`、`RequiresTarget`、`ExplicitOrderOnly` 和通用 `Flags` 数组没有运行语义，均不再属于 schema，也没有别名或兼容读取。
- `KeepRunningUntilFinished` 是唯一保留的 decision 布尔字段。省略或写 `false` 表示实体接纳 Order 后提交任务完成；写 `true` 表示继续等待同一 `OrderId` 的终态回执。
- ability 意图只由 decision/task 的 `AbilityKey` / `AbilityId` 与 `AbilitySlotIndex` 合并得到。只写 ability 或只写 slot、以及 decision 与 task 互相冲突，都会在加载期失败。

```gherkin
Feature: AI 配置只暴露真正可执行的任务

  Scenario: Mod 声明尚未支持的并行任务
    Given Mod 的 AI tasks 配置把 Kind 写成 Parallel
    When 游戏加载该 Mod
    Then 加载失败并说明 Parallel task 尚未支持
    And AI 不会把它当作 SubmitOrder 提交指令
```

## GraphScore 只读契约

`GraphScore` 只能观察世界并计算候选分数，不能借评分过程改变世界：

- 允许项是 Core 明确列出的纯读取 opcode，包括常量、计算、比较、世界/属性/Tag/黑板/关系读取、查询、过滤与聚合。新增 opcode 默认禁止，必须先明确分类。
- effect/state 写入、关系写入、黑板写入、实体生命周期事务和 `InvokeBuiltin` 一律在 AI 配置编译期报错；需要改变世界时，应由候选胜出后提交的 Order、执行 graph 或 effect 步骤承担。
- 评分图在配置编译期完成校验并复制进 `UtilityAiCompiledRuntime`。候选热路径只执行这份冻结程序，不重新扫描 opcode，也不从可变注册表重新取程序。
- 只要配置包含 `GraphScore`，装配 `UtilityAiDecisionSystem` 时就必须同时提供 `GraphProgramRegistry` 和 `IGraphRuntimeApi`，且注册表中仍有对应 graph id。缺失时启动失败，不能把分数静默改成 `0`。
- 评分执行只使用栈上寄存器和固定容量目标列表，不在候选热路径分配内存，也不做 ECS 结构变更。

## 一次完整思考的总预算

每个 `AI/profiles.json` profile 必须明确填写两个正数预算：

- `MaxCandidates`：一个 actor 一次完整 Utility 思考中，从 target acquisition 取得并准备逐目标检查的 `decision × target` 总数。
- `MaxGraphScoreInstructions`：该次思考中所有 `GraphScore` 调用实际执行的 VM 指令总数。

二者都按完整思考计数，不会按 decision maker、decision、target filter、target、consideration 或单次评分图调用重新开始。字段缺失、零、负数、拼写错误或旧别名都在加载期报错，没有默认值。

候选计数的唯一扣减点是：target acquisition 产出一个待检查目标之后、首个逐目标 filter op 之前。每个 `decision × target` 只扣一次，随后才能执行 target filter、ability eligibility、readiness、priority bucket、consideration 或 GraphScore；因此被 filter 或 readiness 拒绝的目标也已经消耗预算。达到上限后若还存在下一个待检查目标，立即返回 `CandidateBudgetExhausted`，即使此前所有目标都被 filter 拒绝。

GraphScore 计数发生在现有 `GasGraphOpHandlerTable` 执行循环内。每条真正到达并准备执行的指令消耗一单位，包含 NOP、jump 以及循环中重复执行的指令；被 jump 跳过的指令不计数。所有评分图共享调用方持有的同一个计数器，同时每次调用仍受 `GraphVmLimits.MaxInstructionsPerExecution` 防失控保险约束。

达到上限后恰好结束完整思考是成功；只有还需要下一个候选或下一条图指令时才算耗尽。结果合同如下：

- `CandidateSelected`：完整遍历结束并有胜出候选，可以进入 Order 提交。
- `NoCandidate`：完整遍历结束但没有可用候选。
- `CandidateBudgetExhausted`：候选遍历不完整。
- `GraphScoreInstructionBudgetExhausted`：评分图工作不完整。
- `TargetScratchCapacityExhausted`：配置的固定目标 scratch 无法容纳查询结果。

后三种结果一律 fail-closed：不提交部分最佳候选，不产生 Order。`UtilityAiDecisionTrace` 记录结果类型、候选消费量/上限和 GraphScore 指令消费量/上限，并清空本次最佳候选展示，避免把不完整结果误看成完整决策。

空间查询来源的 target acquisition 先按既有空间查询合同完成确定性排序与去重，再把目标交给候选计数点。这个查询与排序阶段不受 `MaxCandidates` 限制；`MaxCandidates` 只限制其后的逐目标 filter/readiness/评分工作。物理 scratch 容量由已编译 target filter 的 `MaxResults` 在系统装配时建立，配置重载会重新装配；空间查询溢出返回 `TargetScratchCapacityExhausted`，不能截断后继续检查。候选、图执行与追踪热路径使用固定数组、栈上值和 inline ECS query，预热后不分配托管内存。

### Cucumber UAT

```gherkin
Feature: 大规模战斗中的自动施法不会因目标数量失控

  Scenario: 可见目标超过单位的一次思考预算
    Given 法师的自动施法配置允许一次评估 32 个候选
    And 战场上存在足够多的合法目标，使法师还需要评估第 33 个候选
    When 法师进行下一次自动施法思考
    Then 法师不会根据不完整比较提交施法命令
    And AI 检查器显示本次结果为候选预算已耗尽
    And AI 检查器显示候选消费量为 32/32
    And 相同战场状态下重复思考得到相同结果

  Scenario: 前 32 个目标全部被筛选条件拒绝
    Given 法师的自动施法配置允许一次检查 32 个目标
    And 战场上有 33 个会被当前目标条件拒绝的单位
    When 法师进行下一次自动施法思考
    Then 法师不会继续检查第 33 个单位
    And 法师不会提交施法命令
    And AI 检查器显示本次结果为候选预算已耗尽
    And AI 检查器显示候选消费量为 32/32

  Scenario: 多次短评分仍共享一个总工作上限
    Given 法师会为多个目标运行评分图
    And profile 的评分图总指令预算不足以完成最后一次评分
    When 法师进行下一次自动施法思考
    Then 法师不会提交来自部分评分结果的施法命令
    And AI 检查器显示本次结果为评分图指令预算已耗尽
    And AI 检查器显示实际执行指令数等于配置上限

  Scenario: 工作量恰好落在配置边界
    Given 法师完成全部候选评分恰好需要 profile 允许的指令数
    When 法师进行下一次自动施法思考
    Then 本次思考正常完成
    And 法师可以提交完整比较后的施法命令
```

## GAS 与 GCD 契约

能力执行仍归 GAS：

- cost、冷却、activation precondition、block tag、damage、effect、projectile 都由 GAS 负责。
- 冷却的唯一真相是 ability exec 中的 `TagClip` 及其 `TimedTag` 生命周期；同一能力或共享组能力通过 `blockTags.blockedAny` 阻止对应 Tag。
- AI / 行为包只读取 ability 的 `blockTags` 可用性并提交 Order；不得保存冷却 Tag、到期步数或另一份冷却时长。
- `DecisionRepeatDelaySteps` 只控制 Utility 仲裁多久后可再次选择同一 decision。它不能表示能力冷却，也不能从 GAS Tag 推导；不需要决策节流时应省略。

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
- `CombatStanceState` 属于 `CombatStanceBehaviorMod`，由该 Mod 注册 authoring。数据写 `{ "stance": "ReturnFire" }`，stance 只能是 `HoldFire` / `ReturnFire` / `Defend` / `AttackAnything`；`stanceId` / `Stance` 数字字段直接报错。
- `AI/profiles.json` 的默认 stance 写 `DefaultStance` 字符串 key；`DefaultStanceId` 数字字段直接报错。

`ComponentRegistry.Apply` 的 fail-fast 信息必须带上组件挂载上下文。模板路径通过 `ConfigConflictReport` 的 winner source URI 传入，map / runtime spawn 路径传入 map id、entity instance 或 template id，方便定位未知 profile、stance、bucket、actuator 等引用。

## 代码锚点

- `src/Core/Gameplay/AI/Config/AiConfigLoader.cs`：AI 配置加载与引用 fail-fast。
- `src/Core/Gameplay/AI/Systems/UtilityAiSystems.cs`：Order intent 提交与正式准入/终态回执消费。
- `src/Core/Gameplay/AI/Utility/UtilityAiGraphSafety.cs`：GraphScore opcode 的显式纯读取分类。
- `src/Core/Gameplay/AI/Utility/UtilityAiCompiledRuntime.cs`：验证后评分图程序的冻结快照。
- `src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`、`GasGraphOpHandlerTable.cs`：跨多次执行共享、按实际执行指令扣减的通用预算合同。
- `src/Core/Gameplay/GAS/Orders/OrderTypeRegistry.cs`：Order type key/id SSOT。
- `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`：ability id 到执行定义的注册表。
- `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs`、`TimedTagBuffer.cs` 与 `AbilityActivationBlockTags.cs`：限时 Tag 和激活阻止条件的 GAS 原语。

## 已实现快照

- `AiConfigLoader` 已将 Utility AI authoring 编译为 SoA runtime arrays：`profiles`、`decision_makers`、`decisions`、`considerations`、`target_filters`、`target_filter_ops`、`inputs`、`normalizations`、`curves`、`tasks`、`stances`、`actuators`。
- Utility AI 配置引用在加载期 fail-fast。未知 order type、ability、graph、tag、atom、input、filter、curve 或本地 runtime 引用都会报错，不 fallback 到 `0`。
- `GameEngine` 在 order type、ability、graph 注册后重建 `AiRuntime`，并将 Utility AI 接入主循环：`InputCollection` 做 think scheduling，`PostMovement` 在 spatial refresh 后、`OrderBufferSystem` 前做 decision/order intent submit，`Cleanup` 在终态生产者之后消费 Order 回执并清理 combat memory。
- Utility AI runtime 只提交 order intent。它可以根据配置提交 `moveTo`、`attackTarget` 或其它 order，但不发布 `EffectRequest`、不扣 mana、不写 cooldown、不绕过 GAS 校验。
- `ActuatorReadiness` 与 `AimGate` 是 AI Core 的通用 gate；它们和 GAS activation block tag、activation precondition 进入同一 autocast 候选流水线，不把炮台等业务词带入 Core。
- `mods/CombatStanceBehaviorMod` 是 Combat stance 业务行为包。它拥有 `attackMove`、`assaultMove`、`guard`、`setCombatStance`、`scatter`，并把这些业务 order 转换为已有基础 order intent，例如 `moveTo` 与 `attackTarget`。
- AI Inspector 会打印 Utility AI runtime 表规模，并读取 opt-in `UtilityAiDecisionTrace` 输出思考结果、候选与评分图预算消费、最佳 decision、readiness block、task status、最后提交的 order/ability。
