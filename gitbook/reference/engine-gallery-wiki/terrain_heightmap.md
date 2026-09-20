# 海拔越高颜色越浅的岛屿

一整幅 480m 的程序化岛屿高度场按「绝对海拔」上色：水下陆架、海平面、雪线各占一条色带，相机拉到 620m 高俯瞰全岛。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_terrain_heightmap/poster.png" src="artifacts/evidence/engine_raylib_terrain_heightmap/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_terrain_heightmap/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `terrain_heightmap` |
| preset | `engine_raylib_terrain_heightmap` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/TerrainHeightmapScene.cs` |
| 承接渲染器 | `RaylibContinuousHeightmapRenderer`（`IContinuousHeightmapRenderSource` 合同） |
| 注册表条目 | `engine_raylib_terrain_heightmap`（`showcase.registry.json`，tier T1） |

`RaylibContinuousHeightmapRenderer` 消费高度场源合同（本场景：16×16 chunk、每 chunk 33 采样、480m 世界、seed 47 的岛屿场），按绝对海拔色带着色——`AbsoluteColorSeaLevelCm`/`AbsoluteColorPeakSpanCm` 来自源的渲染档（`RenderProfile`），不硬编码。宿主里这条车道承担「超密高度场降采样」的大地图远景渲染。

## 这场演的是什么

- 色带按绝对海拔：水下陆架最深、海平面处分界、往峰顶逐级变浅——同一函数跨 chunk 连续。
- `VisibleRadiusCm = 90_000`（900m）可见半径裁剪，配合超密降采样演示「大范围、低绘制」取舍。
- 相位 0.40±0.14 慢摆，岛体进阴影深度 pass（投影半径 360m）。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/terrain_heightmap.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 2.13 | 1.38 | 149.27 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_terrain_heightmap --adapter raylib
```

## 边界与深读

- 本场是「视觉高度图」远景车道；可交互地表着色（chunk 网格 + 顶点色）见 [一座岛的皮肤是顶点色刷的](terrain_surface.md)。
- 地形源合同与降采样语义：[Logic Terrain and Topology](../logic-terrain-and-topology.md)。
