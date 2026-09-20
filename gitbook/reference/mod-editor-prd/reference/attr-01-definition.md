# attr-01 reference · 属性定义与约束

> 现状参考。第一性需求见 [attr-01 PRD](../prd/attr-01-definition.md)；配置说明见 [attr-01 配置说明](../config/attr-01-definition.md)。

## 1. 现状快照

- MaxAttributes=64、id 从 0 连续、InvalidId=-1、Ordinal；冻结在启动序列末尾。
- 约束表（DeepObject）：引擎默认 5 属性，均 clampToBase+min0；`SetConstraints(名字)` 对未注册名隐式注册。
- 热替换三限制（id 已注册/旧约束非空/新约束非空），唯一消费方为工作台热管线。
- 扩展属性区（10001-20000）三件套无生产调用方且 id 进不了 64 槽缓冲——死链路（T16）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 上限/InvalidId | src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs:8-12 |
| 表参数 | src/Core/Registry/ModRegistrySet.cs:75-82 |
| 约束结构与工厂 | AttributeRegistry.cs:106-134 |
| 隐式注册 | AttributeRegistry.cs:95-104 |
| 热替换三限制 | AttributeRegistry.cs:67-93 |
| 扩展区死链路 | ExtensionAttributeRegistry.cs:12,19,40-43；AttributeSchemaUpdateQueue.cs:16-28 |
| 冻结点 | src/Core/Engine/GameEngine.cs:1674 |
| 约束现状 | assets/GAS/attribute_constraints.json |

**相关文档**：[attr-01 PRD](../prd/attr-01-definition.md) · [attr-02 reference](attr-02-modifiers.md)
