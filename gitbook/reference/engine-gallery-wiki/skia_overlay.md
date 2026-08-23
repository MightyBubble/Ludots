# 3D 画面上贴一块 2D 仪表盘

3D 场景照常渲染，`RaylibSkiaRenderer` 在上面合成 Skia 画出来的 HUD：标题面板、96 帧帧时柱状图、脉动罗盘——GPU 2D 矢量绘制，不是贴图 UI。

<img src="artifacts/acceptance/engine_gallery_all/skia_overlay.png" alt="skia_overlay 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `skia_overlay` |
| preset | `engine_raylib_skia_overlay` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/SkiaOverlayScene.cs` |
| 承接渲染器 | `RaylibSkiaRenderer` + `SkiaRasterLayer`（分层光栅） |
| 注册表条目 | `engine_raylib_skia_overlay`（`showcase.registry.json`，tier T1） |

绘制走 `SKCanvas` 原语（圆角矩形、线、文字），`SkiaRasterLayer` 分层光栅后 `DrawTo` 合成到 `RaylibSkiaRenderer` 的画布，`RenderToScreen` 一次性上屏。字体直接 `SKTypeface.FromFamilyName("Consolas")`。宿主侧同一渲染器承接 Skia 覆盖层合同；UI 面板四种表面的分工见 [UI 面板作者形态](../../architecture/ui-panel-authoring-form.md)。

## 这场演的是什么

- 左上仪表盘：标题、当前帧耗时、96 帧滚动柱状图（0–40ms 刻度，超界钳制）——帧时历史当场可读。
- 右上罗盘外圈呼吸脉动、指针随时间旋转——动画曲线在 Skia 侧驱动。
- 底下 3D 层是八个浮动立方与金球，正常进光照与阴影，两层互不干扰。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/skia_overlay.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 3.45 | 4.31 | 31.12 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_skia_overlay --adapter raylib
```

## 边界与深读

- Skia 层是覆盖层（overlay）语义：不做布局系统，复杂 UI 面板走 UI Runtime 的面板形态。
- 深读：[UI 渲染控制与 Surface 所有权](../../architecture/ui-rendering-and-surface-ownership.md)。
