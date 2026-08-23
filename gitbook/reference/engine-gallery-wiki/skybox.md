# 头顶的天空是画出来的渐变

`RaylibSkyboxRenderer` 用天顶/地平线/地面雾三色渐变加太阳圆盘画出整个天空，七根柱子绕成一圈，太阳方位随时间慢慢绕行。

<img src="artifacts/acceptance/engine_gallery_all/skybox.png" alt="skybox 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `skybox` |
| preset | `engine_raylib_skybox` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/SkyboxScene.cs` |
| 承接渲染器 | `RaylibSkyboxRenderer`（`skybox` 着色器） |
| 注册表条目 | `engine_raylib_skybox`（`showcase.registry.json`，tier T1） |

天空颜色走 `RaylibRenderEnvironmentConfig`（zenith/horizon/groundHaze/clear 色 + 尺寸），宿主与画廊同一配置树；太阳盘/光晕四参数由 `RaylibSkyboxConfig` 驱动，`skybox` 与 `sky_daynight` 两条天空着色器共享 `sun_disk.glsl.inc` 单一来源。mod 作者改环境 JSON 即改天空，没有第二个入口。

## 这场演的是什么

- 七根双色柱（深色柱身 + 暖色顶块）绕半径 26m 一圈，全部投方向光阴影。
- 相位在 0.38–0.66 白昼弧段缓慢推进（`SkyboxScene.Draw`），太阳与阴影随之绕行。
- 天空盒尺寸 1200m，无贴图——整片天空是渐变函数直出。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/skybox.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 0.56 | 0.23 | 35.50 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_skybox --adapter raylib
```

## 边界与深读

- 全昼夜循环与渐变停靠点见 [四十八秒过完一整天](sky_daynight.md)。
- 深读：[渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)。
