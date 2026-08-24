# 二十排方碑走进雾里

方碑、圆球、矮台沿视线一排排远去，越远越融进雾色——距离雾的衰减曲线和环境色调对天空的接管一目了然。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_atmosphere_fog/poster.png" src="artifacts/evidence/engine_raylib_atmosphere_fog/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_atmosphere_fog/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `atmosphere_fog` |
| preset | `engine_raylib_atmosphere_fog` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/AtmosphereFogScene.cs` |
| 承接渲染器 | `RaylibRenderEnvironmentRenderer`（组帧）+ 雾参数来自 `RaylibFrameLighting` |
| 注册表条目 | `engine_raylib_atmosphere_fog`（`showcase.registry.json`，tier T1） |

雾不是场景私货：`FogColor`/`FogNearMeters`/`FogFarMeters`/`FogDensity` 全部来自光照总线（`distance_fog.json` 环境配置），场景只负责把总线参数注入环境配置并把天空地平线色染成雾色。mod 作者改环境 JSON 的雾条目，全场景雾随之变化，无逐 shader 开关。

## 这场演的是什么

- 20 组道具列从 z=-20m 每 34m 一排铺到 -666m，每组一方碑（10×32×10）一圆球一矮台，五色调色板循环。
- 相机看向远处，近排清晰、远排逐级没入雾色——直接读出 FogNear→FogFar 的过渡带。
- 天空地平线与地面雾色被 `FogColor` 接管（`AtmosphereFogScene.BuildEnvironmentConfig`），雾与天空不是两张皮。
- 全列进阴影深度 pass，太阳相位固定 0.40。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/atmosphere_fog.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 3.80 | 0.69 | 408.83 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_atmosphere_fog --adapter raylib
```

## 边界与深读

- 高度雾/体积光不在词汇里：合同只有距离雾四参数。
- 深读：[Raylib 最小引擎能力总览](../../architecture/raylib-engine-capabilities.md)的「光照与 IBL」节。
