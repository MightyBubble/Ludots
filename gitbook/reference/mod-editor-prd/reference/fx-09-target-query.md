# fx-09 reference · 目标查询

> 现状参考。第一性需求见 [fx-09 PRD](../prd/fx-09-target-query.md)；配置说明见 [fx-09 配置说明](../config/fx-09-target-query.md)。

## 1. 现状快照

- BuiltinSpatial 五形状互斥矩阵：Circle 需 radius>0；Cone 需 radius+halfAngle>0；Rectangle 需 halfWidth+halfHeight>0（rotation 可选）；Line 需 length>0+halfWidth>0；Ring 需 radius>0 且 0≤innerRadius<radius；origin 取 Default/Source。
- GraphProgram 查询：九个空间字段全禁、graphProgramId>0 必填。
- 查询中心解析：Cone/Line/Rectangle 偏 source；其余先 target 点再 source 兜底。
- BuiltinSpatialDescriptor 的 RelationFilter/ExcludeSource/MaxTargets/LayerMask 四字段从未被 loader 填充（双路径残留，todo/effect.md E2）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 五形状互斥矩阵 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1667-1727 |
| origin 二值 | EffectTemplateLoader.cs:1729-1743 |
| GraphProgram 字段禁用 | EffectTemplateLoader.cs:1648-1661 |
| 查询中心解析 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:132-155 |
| 描述符死字段 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:126-129 |

**相关文档**：[fx-09 PRD](../prd/fx-09-target-query.md) · [fx-10 reference](fx-10-target-filter.md)
