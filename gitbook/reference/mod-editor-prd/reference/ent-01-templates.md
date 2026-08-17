# ent-01 reference · 实体模板

> 现状参考。第一性需求见 [ent-01 PRD](../prd/ent-01-templates.md)；配置说明见 [ent-01 配置说明](../config/ent-01-templates.md)。

## 1. 现状快照

- 模板 = id + onSpawnEffect + components（组件名→原始 JSON 的开放映射），经配置目录表加载（`Entities/templates.json`，数组按 id 合并）。
- 实例化：地图布阵（Template + InstanceId + Overrides）与效果造单位共用装配路径；实例覆盖逐组件写入。
- 出生效果在组件就绪后施放。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 模板形状（id/onSpawnEffect/components） | src/Core/Config/EntityTemplate.cs |
| 目录登记（Entities/templates.json，数组按 id） | assets/config_catalog.json |
| 装配与实例化 | src/Core/Config/EntityBuilder.cs |
| 真实模板表 | mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Entities/templates.json |

**相关文档**：[ent-01 PRD](../prd/ent-01-templates.md) · [map-01 reference](map-01-definition.md)
