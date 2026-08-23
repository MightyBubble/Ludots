# map-02 reference · 地图触发器

> 现状参考。第一性需求见 [map-02 PRD](../prd/map-02-triggers.md)；配置说明见 [map-02 配置说明](../config/map-02-triggers.md)。

## 1. 现状快照

- 地图 `TriggerTypes` 为字符串数组：全限定类型名；进地图时反射解析，实例化为触发器基类子类并注册。
- 多片段合并取并集；解析不到的类型跳过（与代码先行路径一致），JSON 路径不因单个坏类型整体失败。
- 代码先行（MapDefinition）与 JSON 两条源在同一装载点汇合。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| JSON 地图触发器反射装载（含跳过语义） | src/Core/Engine/GameEngine.cs:2789-2800 |
| 代码先行装载 | src/Core/Engine/GameEngine.cs:2772-2788 |
| TriggerTypes 字段 | src/Core/Config/MapConfig.cs:49 |
| 真实启用实例 | mods/AuditPlaygroundMod/assets/Maps/audit_outer.json |

**相关文档**：[map-02 PRD](../prd/map-02-triggers.md) · [map-01 reference](map-01-definition.md)
