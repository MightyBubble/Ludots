# ab-05 reference · 激活门

> 现状参考。第一性需求见 [ab-05 PRD](../prd/ab-05-activation-gates.md)；配置说明见 [ab-05 配置说明](../config/ab-05-activation-gates.md)。

## 1. 现状快照

- 直接激活入口 AbilitySystem.TryActivateAbility 判序：存活 → AbilityStateBuffer → 目标校验 → 槽位 → blockTags → 前置图（validationTarget）→ 进度需求；生产调用方仅 ReactionSystem。
- 目标校验：TargetContext 存活、显式目标存活、目标集合非空且全存活；validationTarget = 显式目标，否则集合首，否则施法者。
- tag 门评估：施法者无 tags 时仅空 requiredAll 通过；否则 !Intersects(BlockedAny,Effective) ∧ ContainsAll(RequiredAll,Effective)。
- 订单起播入口（AbilityExecSystem）差异：toggle 先关（即便再激活冷却在场）；进度需求（use）先于前置图，前置图带目标坐标；进度需求可延迟评估（显式范围 + 无存活 TargetContext + 首 item 为 InputGate/TargetCollectionGate 时挂起，门响应回填后再判；挂起期遇非门 item 直接失败）。
- 起播失败映射：PreconditionFailed → 同名；其余 → ActivationBlocked。
- 模板实体路径：无技能定义时门与前置可取模板实体上的组件（AbilityActivationBlockTags 等）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 直接激活判序 | src/Core/Gameplay/GAS/Systems/AbilitySystem.cs:102-192 |
| 目标校验 | AbilitySystem.cs:256-297 |
| tag 门评估器 | src/Core/Gameplay/GAS/Systems/AbilityActivationBlockTagEvaluator.cs:8-22 |
| 直接激活唯一生产调用方 | src/Core/Gameplay/GAS/Systems/ReactionSystem.cs:59 |
| toggle 先关 | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:215-231 |
| blockTags 拒（起播） | AbilityExecSystem.cs:236-257 |
| 进度需求与延迟挂起 | AbilityExecSystem.cs:282-298、695-750 |
| 前置图评估（带坐标） | AbilityExecSystem.cs:324-346 |
| 失败映射 | AbilityExecSystem.cs:752-756 |
| 挂起期非门 item 失败 | AbilityExecSystem.cs:897-903 |

**相关文档**：[ab-05 PRD](../prd/ab-05-activation-gates.md) · [ab-04 reference](ab-04-cooldown.md)
