# RFC-0060 AI Utility Autocast 契约收敛

状态：Accepted for issue #225 implementation slice

正式结论：`gitbook/architecture/ai-utility-autocast-contract.md`

## 背景

Epic #224 要把现有 `src/Core/Gameplay/AI` 深化为通用 Utility AI + OpenRA Stance 行为包。#225 先处理契约前提，不实现 scoring、target acquisition、autocast 仲裁、stance/order 行为包或主循环接入。

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
7. GCD = 共享 cooldown tag，复用 `AbilityCooldown.CooldownTagId` 与 `AbilityActivationBlockTags`。
8. ability 引用若出现在 AI 配置中，必须通过 `AbilityDefinitionRegistry` 校验，未知 id/key 加载期报错。

## 复用清单

- ConfigPipeline / ConfigCatalog：AI 配置继续走正式配置管线。
- `AiConfigLoader`：现有 AI 配置编译入口，作为 #225 契约收敛点。
- `OrderTypeRegistry`：Order type key/id 的注册与查询入口。
- `AbilityDefinitionRegistry`：ability id 到 GAS 执行定义的注册表。
- `OrderQueue` / `OrderBufferSystem`：AI intent 的正式入口。
- GAS ability components：cooldown、block tags、activation precondition 继续由 GAS 管理。

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

## Epic #224 后续落地

#225 的契约切片已被后续实现复用：

- Utility AI 编译结果进入 `AiCompiledRuntime.UtilityRuntime`。
- target acquisition、decision-target evaluator、共享 cooldown 仲裁、order task submission、actuator gate、主循环接入由 `src/Tests/GasTests/UtilityAiRuntimeTests.cs` 覆盖。
- OpenRA stance 业务行为留在 `mods/OpenRaStanceBehaviorMod`，不进入 AI Core；该 Mod 通过 GAS/order config 声明业务 order，并只提交 `Order` intent。
- AI Inspector 补充 Utility AI runtime inventory 与 opt-in trace 摘要，用于调试候选、过滤原因、分数、task status 和已提交 order。
