# attr-06 reference · 属性事件

> 现状参考。第一性需求见 [attr-06 PRD](../prd/attr-06-events.md)；配置说明见 [attr-06 配置说明](../config/attr-06-events.md)。

## 1. 现状快照

- 映射注册表：静态类，int[64] 直查表；Register 同时注册属性与事件 tag；GetEventTagId 越界返回 InvalidId。注册入口生产代码零调用——唯一调用方是测试，默认运行时映射表全空。
- 玩法面链路：任意属性写打属性脏位（写入权威与聚合器两处）。写入权威在当前值实际变化时把 Source 记进 DirtyFlags 的 per-attribute 槽；聚合器只打脏位，不覆盖该槽。延迟触发收集逐位与 AttributeLastSnapshot 比较得 Old/New，填 Source 入队并清位清槽更新快照；无 snapshot 先补建且 OldValue=0。快照来源：实体模板创建与批量生成器。处理系统查映射，非无效 id 则事件总线发布 GameplayEvent（TagId、Source=触发器来源、Target=变化实体、Magnitude=NewValue）；无映射直接 return——当前内容下生产映射表仍为空，测试与显式注册才会发布。
- 表现面链路（并行）：变化位由写入权威与聚合器打点；表现投影系统对置位属性产出 PresentationEventKind.AttributeValueChanged 与 PresentationOwnerChange；清位系统消费后移除组件。
- 两面差异事实：玩法面带 Old/New/Source、延迟一帧；表现面只报变化事实、当帧消费即清。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 映射注册表 | src/Core/Gameplay/GAS/Registry/AttributeEventTagRegistry.cs:5-24 |
| 唯一注册调用方（测试） | src/Tests/GasTests/GasCore/DeferredTriggerProcessEventTests.cs:23 |
| 写入打脏并记 Source | src/Core/Gameplay/GAS/AttributeMutationOps.cs；DirtyFlags.RecordAttributeSource |
| 收集比对、填 Source 与快照补建 | src/Core/Gameplay/GAS/Systems/DeferredTriggerCollectionSystem.cs |
| 发布（Source 取触发器）与无映射返回 | src/Core/Gameplay/GAS/Systems/DeferredTriggerProcessSystem.cs |
| 快照来源（模板创建） | src/Core/Config/ComponentRegistry.cs:951-961 |
| 快照来源（批量生成） | src/Core/Config/TemplateEntityBatchSpawner.cs:254,305,610-612 |
| 表现位打点 | AttributeMutationOps.cs:218-226；AttributeAggregatorSystem.cs:254-257,300-315 |
| 表现投影消费 | src/Core/Presentation/Systems/GameplayPresentationProjectionSystem.cs:282-319 |
| 清位系统 | src/Core/Gameplay/GAS/Systems/ClearPresentationFlagsSystem.cs:48-57 |
| 投影注册 | src/Core/Engine/GameEngine.cs:1861 |

**相关文档**：[attr-06 PRD](../prd/attr-06-events.md) · [attr-03 reference](attr-03-aggregation.md)
