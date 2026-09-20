# map-01 reference · 地图定义

> 现状参考。第一性需求见 [map-01 PRD](../prd/map-01-definition.md)；配置说明见 [map-01 配置说明](../config/map-01-definition.md)。

## 1. 现状快照

- 地图走独立资产管线：按地址收集 `Maps/<id>.json` 全部来源片段，地图专属合并后加载；不走配置目录。
- 合并语义：Entities/Teams/Players/参与者关系**追加**；Boards **按名覆盖**；TriggerTypes 并集；DefaultCamera 后到者赢；Tags 合并。
- MapConfig 含继承字段（ParentId/Dependencies）；布阵实体 = 模板 + InstanceId + 组件覆盖。
- 代码先行（MapDefinition）与 JSON 两条装载源共用同一配置对象。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 片段收集（`Maps/<mapId>.json`） | src/Core/Map/MapManager.cs:99-107 |
| 地图合并（追加/按名覆盖/并集/后到赢） | src/Core/Map/MapManager.cs:194-310 |
| MapConfig 全字段（继承/布阵/棋盘/相机/触发器类型） | src/Core/Config/MapConfig.cs:12-60 |
| 触发器类型反射装载 | src/Core/Engine/GameEngine.cs:2789-2800 |
| 真实地图实例 | mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Maps/rts_red_alert_like.json |

**相关文档**：[map-01 PRD](../prd/map-01-definition.md) · [ent-01 reference](ent-01-templates.md) · [map-02 reference](map-02-triggers.md)
