# 一座岛的皮肤是顶点色刷的

程序化 chunk 地形按海拔分带着色：沙滩、草地、岩石分层出现，低洼处蓄出湖面。这是 Ludots 地表着色车道的最小完整演示。

<img src="artifacts/acceptance/engine_gallery_all/terrain_surface.png" alt="terrain_surface 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `terrain_surface` |
| preset | `engine_raylib_terrain_surface` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/TerrainSurfaceScene.cs` |
| 承接渲染器 | `RaylibTerrainRenderer`（`terrain.fs`）+ chunk 网格源 `ITerrainChunkMeshSource` 合同 |
| 注册表条目 | `engine_raylib_terrain_surface`（`showcase.registry.json`，tier T1） |

渲染器不问地形从哪来——只消费 `ITerrainChunkMeshSource`。画廊用程序化 chunk 源（32×32 chunk、14m 间距、4 quadr/chunk、岛屿模式 seed 23），宿主里同一渲染器接 Core 地形投影；`VisibleRadius`/`SimplifiedCliffRadius` 控制裁剪与悬崖减面。作者面的地形语义见 [Logic Terrain and Topology](../logic-terrain-and-topology.md)。

## 这场演的是什么

- 海拔分带顶点色：高度场同一张，颜色按高度区间分层刷进顶点。
- 低洼水面网格由地形源一并发出（`emitWater: true`），湖面与地形严丝合缝。
- 相位 0.38±0.12 缓慢摆动，晨昏光下分带色的变化可见；地形整体进阴影深度 pass。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/terrain_surface.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 2.67 | 3.26 | 70.75 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_terrain_surface --adapter raylib
```

## 边界与深读

- 本场是地表着色（surface）；按高度场整幅渲染的视觉高度图见 [海拔越高颜色越浅的岛屿](terrain_heightmap.md)。
- 反射水面（海）见 [水面上下各画一遍，合成一片海](water.md)——本场湖面不进反射通道（`ClearReflectiveWater`）。
