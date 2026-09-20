# 场景怎么写

场景文件是 JSON，放在工程的 `scenes/` 目录。最好的学法是抄：打开 `projects/engine_gallery/scenes/composition.scene.json`（岛屿 + 石头阵 + 巡逻兵），照着改。这页用它的骨架逐块讲。

## 一个场景的骨架

```json
{
  "schemaVersion": 1,
  "id": "my_level",
  "title": "我的第一关",
  "summary": "一句话介绍，菜单里显示用",
  "camera": { "mode": "orbit", "target": [0, 8, 0], "distance": 200, "pitchDegrees": 26, "yawDegrees": 45, "fovyDegrees": 45 },
  "assets": [ ],
  "rootNode": "ground",
  "nodes": [ ]
}
```

各字段的人话版本：

| 字段 | 干什么的 |
|---|---|
| `id` | 场景的调用名，菜单、命令行、验收都用它，工程内不能重名 |
| `title` / `summary` | 菜单里显示的名字和一句话简介 |
| `camera.target` | 开场镜头盯着哪个点（ xyz，单位米） |
| `camera.distance` / `pitchDegrees` / `yawDegrees` | 镜头离目标多远、俯仰多少度、绕着转多少度——开场画面由这三个数定 |
| `world.bounds` | 这一关的世界大小范围，给渲染裁剪用，一般抄现成的 |
| `assets` | 本场景用到的外部文件清单（模型、材质），没用就留空 |
| `nodes` | 场景里摆的东西，见下 |

## 摆东西：节点与组件

每个"东西"是一个**节点**，节点上挂**组件**（组件决定它是地形、一堆石头还是一个动画角色）。下面摆两样：一块地形基座，加一排石头：

```json
"nodes": [
  { "id": "ground", "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
    "components": [ { "type": "island_terrain",
      "config": { "worldSizeMeters": 480, "dayPhase": 0.46 } } ] },

  { "id": "rocks", "parent": "ground", "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
    "components": [ { "type": "static_mesh",
      "config": { "primitive": "cube", "instances": [
        { "position": [30, 3, 40], "scale": 6, "yawDeg": 45 },
        { "position": [45, 3, 25], "scale": 4, "yawDeg": 120, "color": [0.5, 0.8, 0.5, 1] }
      ] } } ] }
]
```

要点：

- 第一个节点是根（`rootNode` 指它），其他节点用 `parent` 挂靠，形成层级；
- `transform` 是节点自身的位置/朝向/缩放，多数时候保持默认即可；
- 真正干活的是 `components` 里的 `type` 和 `config`——每种组件认哪些配置，见[组件手册](components.md)；
- 石头阵就是在 `instances` 里一行一块：`position` 放哪、`scale` 多大、`yawDeg` 转个角度、`color` 染个色（RGBA 四个数，1 是满）。

## 用外部文件要申报

场景里用到模型或材质文件时，在 `assets` 里声明，再由组件引用——引擎只装申报过的东西，写错文件名会直接报错指出是哪一条：

```json
"assets": [
  { "id": "my_guard", "kind": "model", "source": "Models/mannequin_large_walk.glb" },
  { "id": "my_rock_mat", "kind": "material", "source": "materials/rock.json" }
]
```

## 相机微调的最短路径

觉得开场视角不对？只改 `camera` 三个数：站远了 `distance` 调小；想俯视 `pitchDegrees` 加大；想转个方向 `yawDegrees` 转。保存重跑即可。存档的验收截图用的就是这套初始视角。

写完场景，去 `catalog.json` 加一行 `{ "id": "my_level", "asset": "scenes/my_level.scene.json" }`，菜单里就能选到它。
