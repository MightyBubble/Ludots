# Skia GPU UI Compositor 基准报告

- 场景：`skia_overlay`（engine gallery，1600×900 真实 GL 窗口）
- 帧数：420（gallery app `--frames 420`，官方验收批同规格）
- 采集：`Ludots.App.RaylibPlayer --json`，本机 Windows / 本机 GPU，2026-09-21
- 对照：默认（GPU render-texture 直绘）vs `LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI=1`（光栅 + 整窗纹理上传回退）
- 原始数据：`skia_overlay_gpu_default.json` / `skia_overlay_raster_fallback.json`

## 结果

| 配置 | avg ms | p95 ms | max ms | wall ms |
|---|---|---|---|---|
| GPU render-texture（默认） | 0.554 | 0.664 | 76.11 | 288.3 |
| 光栅回退（kill-switch） | 3.614 | 4.749 | 62.08 | 1594.9 |

## 读法

- avg 帧耗时 GPU 路径 0.554ms，光栅回退 3.614ms——该场景每帧全量重绘 HUD，差距主要来自整窗 CPU 光栅与 `UpdateTexture` 上传的消除。
- wall 时间 288ms vs 1595ms：该 gallery 批次不锁垂直同步，GPU 路径吞吐约为回退路径的 5.5 倍。
- max 帧尖刺两者同量级（首帧/窗口初始化），不是 GPU 路径引入的退化。
- 该基准覆盖 gallery 直绘路径（`RaylibSkiaGpuCanvasSurface`）；宿主合成器 UI 面板层走同一表面类与同一脏门控，帧路径成本同构。retained 面板静止帧在两种配置下都跳过重绘（`IsDirty` 门），差距只在脏刷新帧。
