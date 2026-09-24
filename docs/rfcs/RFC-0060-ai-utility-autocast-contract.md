# RFC-0060 AI Utility Autocast 契约收敛

状态：Accepted for issue #225 implementation slice

正式结论：`gitbook/architecture/ai-utility-autocast-contract.md`

## 背景

Epic #224 要把现有 `src/Core/Gameplay/AI` 深化为通用 Utility AI + Combat Stance 行为包。#225 先处理契约前提，不实现 scoring、target acquisition、autocast 仲裁、stance/order 行为包或主循环接入。

SSOT 参考材料来自 `docs/ai-utility-autocast-ssot` 分支：

- `docs/reference/ludots_ai_utility_autocast_ssot_plan.html`
- `docs/reference/ludots_utility_ai_soa_openra_behavior_architecture.html`
- `docs/reference/ludots_targeting_order_gas_gap_analysis.html`

## 决议

1. AI 三层模型固定为 Intent / Behavior / Deliberation。
2. AI Core 只产 Order intent，不直接执行 gameplay side effect。
3. 普攻是一种 autocast ability，不再为攻击建立特例系统。
4. 多个 autocast 争抢共享 GCD、mana 或施法槽时，仲裁器就是 Utility 决策入口。
5. `OrderTypeKey` / `OrderTypeId` 必须以 `OrderTypeRegistry` 为 SSOT；未知引用加载期 fail-fast。
6. `OrderTagId` 不是 AI action 契约字段，必须修正为 `OrderTypeId` 或 `OrderTypeKey`。
7. GCD = GAS ability exec 的 `TagClip` / `TimedTag` 生命周期，并由 `AbilityActivationBlockTags` 阻止激活；AI 不保存 Tag 或到期时间。此条由 #714 纠正。
8. ability 引用若出现在 AI 配置中，必须通过 `AbilityDefinitionRegistry` 校验，未知 id/key 加载期报错。
9. `MaxCandidates` 是一个 actor 一次完整思考共享的 `decision × target` 检查总预算：target acquisition 产出一个待检查目标后，必须先消费预算，才能运行任何逐目标 filter op、ability eligibility、readiness、consideration 或 GraphScore；因此被 filter 拒绝的目标也消耗预算。它不限制空间查询与稳定排序本身，不是 scratch 容量，也不按 decision/filter 重置。
10. profile 必须明确填写正数 `MaxGraphScoreInstructions`；它限制同一次思考中全部 GraphScore 调用实际执行的指令总数。
11. 候选、GraphScore 或固定 target scratch 耗尽时，本次思考 fail-closed，不提交部分最佳候选；类型化结果和消费量写入 `UtilityAiDecisionTrace`。
12. GraphScore 总预算复用现有 `GraphExecutor` / `GasGraphOpHandlerTable`，且不替代 `GraphVmLimits.MaxInstructionsPerExecution` 单次执行保险。
13. `AI/tasks.json` 的 `Kind` 必须显式为 `SubmitOrder`。`Sequence`、`Parallel`、`ParallelComplete` 和未知 kind 加载期拒绝；非法运行时枚举在 Order 提交前抛错，不允许 default 兜底提交。
14. 每个 decision 必须且只能引用一个 task，并编译成单一 `TaskIndex`。不保留会静默忽略后续任务的多 task 合同。
15. decision 支持字段只有 `id`、`TargetFilter`、评分/时长字段、`AbilityKey` / `AbilityId`、`AbilitySlotIndex`、`KeepRunningUntilFinished`、`Considerations`、`Tasks`；`Autocast`、`OrdinaryAttack`、`RequiresTarget`、`ExplicitOrderOnly` 与 `Flags` 数组全部删除且不兼容读取。
16. `KeepRunningUntilFinished` 是直接、类型化的布尔属性，继续驱动 #715 的准入后终态等待；它不再属于通用 flags 位集。
17. ability/slot 意图只由 decision 与单个 task 的显式 ability/slot 字段合并；部分意图或冲突意图加载期失败。

## 复用清单

- ConfigPipeline / ConfigCatalog：AI 配置继续走正式配置管线。
- `AiConfigLoader`：现有 AI 配置编译入口，作为 #225 契约收敛点。
- `OrderTypeRegistry`：Order type key/id 的注册与查询入口。
- `AbilityDefinitionRegistry`：ability id 到 GAS 执行定义的注册表。
- `OrderQueue` / `OrderBufferSystem`：AI intent 的正式入口。
- GAS ability components：`TagClip` / `TimedTag`、block tags、activation precondition 继续由 GAS 管理。

## 本轮新增清单

- `AiConfigValidationContext`：把 `OrderTypeRegistry` 和 `AbilityDefinitionRegistry` 显式交给 AI loader 做加载期引用校验。
- `AiConfigLoader` fail-fast：拒绝未知 order type、未知 ability、未知 atom/op/binding 与旧 `OrderTagId`。
- `AIDemoMod` GOAP action 配置：`OrderTagId` 修正为 `OrderTypeId`。
- GitBook 正式契约页：记录 #225 的分层、普攻=autocast、Order/GAS/GCD 边界。

## 非目标

- 不实现 target acquisition、filter、OpenRA target priority。
- 不实现 Utility scoring、decision-target evaluator、autocast candidate arbitration。
- 不实现 attackMove、guard、setCombatStance、stance runtime。
- 不改变 `castAbility` 现有 slot-index 执行语义。
- 不接入新的 AI systems 到主循环。

## 验收

- 未知 order type id/key 加载期报错。
- 旧 `OrderTagId` 加载期报错。
- 未知 ability id/key 加载期报错。
- 现有 AI loader happy path 正常编译。
- 现有 AI 相关测试不回归。
- 多 decision/filter 共享一个 `MaxCandidates` 计数器；每个 acquisition 产出的目标都在首个逐目标 filter op 前消费一次，被 filter 拒绝也计数；尝试检查超出上限的下一个目标时耗尽并阻止 Order 提交。
- 多次短 GraphScore 执行共享一个实际指令计数器；jump 与循环按实际到达次数计数，恰好到达边界可成功。
- 10k 实体预热后候选计数确定、有界，完整 Utility 思考零托管分配。
- 单个 `SubmitOrder` task 正常加载；未实现或未知 kind、多 task decision、旧 decision flags 与 `Flags` 数组均加载失败。
- 程序化注入非法 task kind 时，在 `OrderQueue` 收到任何提交前明确抛错。

```gherkin
Feature: AI 配置只暴露真正可执行的任务

  Scenario: Mod 使用尚未支持的并行任务
    Given AI 配置声明 Parallel task
    When 游戏加载该 Mod
    Then 加载失败并说明该任务类型尚未支持
    And 不会把它当作普通提交指令继续运行
```

## Epic #224 后续落地

#225 的契约切片已被后续实现复用：

- Utility AI 编译结果进入 `AiCompiledRuntime.UtilityRuntime`。
- target acquisition、decision-target evaluator、order task submission、actuator gate、主循环接入由 `src/Tests/GasTests/UtilityAiRuntimeTests.cs` 覆盖；能力冷却只通过 GAS activation block tag 进入 readiness。
- Combat stance 业务行为留在 `mods/CombatStanceBehaviorMod`，不进入 AI Core；该 Mod 通过 GAS/order config 声明业务 order，并只提交 `Order` intent。
- AI Inspector 补充 Utility AI runtime inventory 与 opt-in trace 摘要，用于调试思考结果、候选/评分图预算、过滤原因、分数、task status 和已提交 order。
- #718 将候选和评分图成本收口为 profile 级完整思考预算；预算耗尽与 scratch 溢出均不发布不完整 Order。
- #719 把 Utility task 合同收口为“一项 `SubmitOrder`”，删除没有消费者的 decision flags、任务偏移状态与常量 task-kind trace；`KeepRunningUntilFinished` 继续使用正式 Order 准入/终态回执。
