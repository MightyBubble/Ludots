# map-01 配置说明 · 地图定义

> 配置写法与行为。第一性需求见 [map-01 PRD](../prd/map-01-definition.md)；编辑器需求见 [UXD](../uxd/map-01-definition.md)；现状见 [reference](../reference/map-01-definition.md)。

## 1. 示例配置

演示场景底座地图（`Maps/rts_red_alert_like.json`）节选：

```json
{
  "Id": "rts_red_alert_like",
  "DefaultCamera": { "TargetXCm": 12000, "TargetYCm": 12000, "Yaw": 180, "Pitch": 54, "DistanceCm": 11800, "FovYDeg": 60 },
  "Boards": [ { "Name": "default", "SpatialType": "Grid", "WidthInMacroTiles": 64, "HeightInMacroTiles": 64, "GridCellSizeCm": 400 } ],
  "Entities": [
    { "Template": "rts_ra_team_anchor", "InstanceId": "allied_team_anchor",
      "Overrides": { "Team": { "Id": 1 }, "WorldPositionCm": { "Value": { "X": 9000, "Y": 13600 } } } }
  ],
  "Teams": [ { "TeamId": 1, "RepresentativeInstanceId": "allied_team_anchor" } ],
  "Players": [ { "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "allied_player_anchor" } ]
}
```

读法：一张网格棋盘、按模板布下带覆盖的实体、绑定队伍与玩家、给一个开局相机。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `Id` | 地图 id；游戏配置 `startupMapId` 指向它 |
| `ParentId` / `Dependencies` | 继承父地图（布阵/棋盘等派生）；进地图时先解析继承链 |
| `Boards[]` | 空间基底：名字 + 类型（Grid）+ 尺寸/格宽；**按名覆盖**（同名棋盘后到者整体替换） |
| `Entities[]` | 初始布阵：`Template`（引用实体模板）+ `InstanceId`（实例唯一名）+ `Overrides`（逐组件覆盖初值）；合并为**追加** |
| `Teams[]` / `Players[]` | 队伍与玩家绑定（代表实体）；追加式 |
| `TriggerTypes[]` | 启用的触发器类型名（见 map-02）；合并为并集 |
| `DefaultCamera` | 开局相机（虚拟相机 id 或显式参数）；后到者赢 |
| `Tags` / `Metadata` | 地图标签与自由元数据 |
| `ContinuousHeightmap*` / `StructureCollision*` / `StructureAware*` | 地形高度、结构碰撞资产引用与开关 |

## 3. 文件结构

`assets/Maps/<地图id>.json`，**不走配置目录**——由地图资产管线加载：按地址收集全部来源片段（引擎默认 + 各 mod）后做地图专属合并。跨 mod 贡献即"同 id 地图的多份片段"。

## 4. 跨 mod 合并规则

| 部位 | 规则 |
|---|---|
| Entities / Teams / Players / 参与者关系 | **追加**（各片段实体都上场；InstanceId 冲突即加载失败） |
| Boards | **按名覆盖**（同名棋盘整块替换——难度修正改棋盘的通道） |
| TriggerTypes | 并集去重 |
| DefaultCamera / Tags | 后到者赢 / 合并 |

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 启动地图不存在 | 启动失败 |
| 布阵引用未注册的模板 | 加载失败，指明实例与模板名 |
| InstanceId 重复 | 加载失败 |
| 继承环 / 父地图缺失 | 加载失败 |

## 6. 实例

- 演示底座地图：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Maps/rts_red_alert_like.json`
- 触发器启用示例：`mods/AuditPlaygroundMod/assets/Maps/audit_outer.json`

**相关文档**：[map-01 PRD](../prd/map-01-definition.md) · [ent-01 配置说明](ent-01-templates.md) · [map-02 配置说明](map-02-triggers.md)
