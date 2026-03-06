# Recipe: 新增地图

## 目标

在 Mod 中定义一张新地图，通过 ConfigPipeline 参与合并。

## 文件清单

```
mods/MyMod/assets/
├── Maps/my_arena.json      ← 地图配置
└── game.json               ← 设置 startupMapId（可选）
```

## 地图配置（Maps/my_arena.json）

```json
{
  "Id": "my_arena",
  "ParentId": null,
  "Tags": ["arena"],
  "DefaultCamera": {
    "PresetId": "Default",
    "DistanceCm": 18000
  },
  "Entities": [
    {
      "Template": "moba_hero",
      "Overrides": {
        "WorldPositionCm": { "Value": { "X": 500, "Y": 300 } }
      }
    }
  ],
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "HexGrid",
      "WidthInTiles": 64,
      "HeightInTiles": 64,
      "GridCellSizeCm": 100
    }
  ],
  "TriggerTypes": []
}
```

## 设置为启动地图（game.json）

```json
{
  "startupMapId": "my_arena"
}
```

## 地图继承

`ParentId` 指向父地图 ID，子地图配置递归合并到父配置之上。`Boards` 按 `Name` 合并。

## 挂靠点

| 基建 | 用途 |
|------|------|
| `MapManager` | `LoadMap(mapId)`，从 VFS 加载 JSON |
| `ConfigPipeline` | `CollectFragments("Maps/{mapId}.json")` 合并多 Mod 片段 |
| `TriggerManager` | 地图加载后触发 `MapLoaded` 事件 |
| `GameEngine` | `engine.LoadMap(mapId)` 入口 |

## 检查清单

*   [ ] `Id` 全局唯一
*   [ ] JSON 放在 `assets/Maps/{id}.json`，路径与 `Id` 一致
*   [ ] Board 至少有一个 `Name: "default"`
*   [ ] 需要地图专属逻辑时，通过 Trigger（`MapLoaded` 事件）注册，不硬编码

参考：`mods/Navigation2DPlaygroundMod/assets/Maps/`、`mods/MobaDemoMod/assets/Maps/`
