# 三枚标记在地形上巡游

圆环、箭头、靶标三枚贴花贴着起伏的地表移动——不是画在贴图上，而是 `decal_project` 着色器沿世界 Y 把贴花投影到接收面网格上，地形起伏处贴花跟着「垂下来」。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_decal_projection/poster.png" src="artifacts/evidence/engine_raylib_decal_projection/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_decal_projection/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `decal_projection` |
| preset | `engine_raylib_decal_projection` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/DecalProjectionScene.cs` |
| 承接渲染器 | `decal_project` 着色器 + `IRaylibReceiverMeshProjector`（接收面投影器） |
| 注册表条目 | `engine_raylib_decal_projection`（`showcase.registry.json`，tier T1） |

图元快照里一条 `GalleryItems.Decal(...)` 就是作者写法的全部：材质行挂贴花贴图，绘制条目给位置/朝向/幅面（`stampWidth`/`stampDepth`）；接收面可插（本场景由 `RaylibVisualHeightmapRenderer` 充当投影器）。脚印、弹坑、选中标记都属于这条车道，见[能力总览](../../architecture/raylib-engine-capabilities.md)的「渲染车道矩阵」。

## 这场演的是什么

- 三枚程序化贴花（128²）：16m 圆环以 0.8 rad/s 自旋、10m 箭头反向转、7m 靶标固定，各自沿地表巡游。
- 贴花严格贴在起伏高度图表面上，坡道处没有「浮空」或「穿模」。
- HUD 报告上一帧绘制的贴花数；贴花不投阴影（覆盖语义不构成遮挡体）。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/decal_projection.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 3.75 | 1.60 | 321.40 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_decal_projection --adapter raylib
```

## 边界与深读

- 投影沿世界 Y 单方向；墙面贴花（法向投影）不在当前词汇里。
- 接收面是合同（`IRaylibReceiverMeshProjector`），地形之外的接收面由宿主另行实现。
