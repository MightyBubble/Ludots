# ab-09 reference · Targeting 与组合命令

> 现状参考。第一性需求见 [ab-09 PRD](../prd/ab-09-targeting.md)；配置说明见 [ab-09 配置说明](../config/ab-09-targeting.md)。

## 1. 现状快照

- 组合计划器 Submit：构建"先移动后施放"计划；NotApplicable 直通、Rejected 拒；保活 actor + 续单状态安装；followUpCast 挂续单缓冲（键=移动单 OrderId，满→RejectedQueueFull）；移动单提交失败回收续单。
- 裁剪：Args.I0=槽位；无 targeting / castRange≤0 / autoTargetPolicy≠None → NotApplicable。
- 排队投影原点：Queued 模式用移动完成后的预计位置；目标点 = 订单目标实体位置，否则空间载荷。
- 移动锚点：距离 ≤ castRange+0.01 已在射程；否则锚点 = actor + 方向×(距离−castRange)。
- followUpCast 强制 Queued；批量命令部分不可行整批拆分报错；取消传播：移动单终态非 Completed → 续单以 Cancelled/Failed 拒绝。
- targeting 编译：castRangeCm 必填非负（0=自施）、impactEffect 必填已注册；旧名 targeting.range、顶层 indicator 专门报错。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 组合计划 Submit | src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs:60-117 |
| 裁剪条件 | CompositeOrderPlanner.cs:245-281 |
| 排队投影原点 | CompositeOrderPlanner.cs:283-307 |
| 移动锚点求解 | CompositeOrderPlanner.cs:319-336 |
| followUpCast 强制 Queued | CompositeOrderPlanner.cs:220-223 |
| 批量拆分报错 | CompositeOrderPlanner.cs:132-170 |
| 取消传播 | src/Core/Gameplay/GAS/Systems/OrderContinuationSystem.cs:88-106 |
| targeting 编译 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:547-594 |
| 真实实例 | mods/showcases/champion_skill_sandbox/.../abilities.json（25 条 targeting，自施例 Garen.Judgment） |

**相关文档**：[ab-09 PRD](../prd/ab-09-targeting.md) · [ab-01 reference](ab-01-definition.md)
