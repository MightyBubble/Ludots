# fx-10 reference · 目标过滤

> 现状参考。第一性需求见 [fx-10 PRD](../prd/fx-10-target-filter.md)；配置说明见 [fx-10 配置说明](../config/fx-10-target-filter.md)。

## 1. 现状快照

- 字段合同：excludeSource 必填布尔；maxTargets 必填整数（0=无限）；relationFilter 必填六值 All/Hostile/Friendly/Neutral/NotFriendly/NotHostile；layerMask 可选字符串数组，经层注册表解析。
- 运行期过滤顺序：ExcludeSource→Ring 内径→LayerMask→Relationship（双方须有 Team，否则滤掉）→容量→根预算。
- 层与敌我在查询描述符侧的同名四字段从未被 loader 填充（E2，fx-09 reference）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 字段校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1745-1758 |
| 六值关系枚举 | src/Core/Gameplay/Teams/RelationshipFilter.cs:51-71 |
| 运行期过滤顺序 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:241-311 |

**相关文档**：[fx-10 PRD](../prd/fx-10-target-filter.md) · [fx-11 reference](fx-11-target-dispatch.md)
