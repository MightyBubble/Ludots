# 四十八秒过完一整天

`RaylibSkyEnvironment` 把六停靠点的渐变烘焙成天空纹理，相位转一整圈：夜→晨光→白昼→黄昏→夜。日光、环境光、阴影跟着相位一起走。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_sky_daynight/poster.png" src="artifacts/evidence/engine_raylib_sky_daynight/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_sky_daynight/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `sky_daynight` |
| preset | `engine_raylib_sky_daynight` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/SkyDayNightScene.cs` |
| 承接渲染器 | `RaylibSkyEnvironment`（渐变烘焙）+ `sky_daynight` 着色器 |
| 注册表条目 | `engine_raylib_sky_daynight`（`showcase.registry.json`，tier T1） |

宿主里这是数据驱动配置：场景用手工 `MergedConfigEntry`（`SkyDayNightScene.Load` 里的 `gallery.daynight` JSON）演示作者写法——`gradientStops` 每站给 `phase`/`zenith`/`horizon`，装载期烘焙成 256×64 渐变纹理。mod 作者在环境配置 JSON 里写同样的条目即可，装载校验 fail-loud。

## 这场演的是什么

- 48 秒一整圈（常量 `CycleSeconds`），六停靠点：0.0 深夜 → 0.24 晨光橙 → 0.38 白昼 → 0.62 白昼 → 0.78 黄昏红 → 1.0 回到深夜。
- `ApplyDayPhase` 推天空、`RaylibFrameLighting.SetDayPhase` 推日光与环境、`SetSun` 把太阳方位/颜色回填天空——三者同一相位源。
- 地面白立方与橙球、六个绕行小方块全部投阴影，阴影方向随太阳转。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/sky_daynight.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 4.01 | 0.21 | 409.30 |

（p95 高是相位步进触发渐变重烘的节流帧，属预期行为。）

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_sky_daynight --adapter raylib
```

## 边界与深读

- 渐变天空是解析函数不是贴图星空；云、星星不在本车道词汇里。
- 对照：[头顶的天空是画出来的渐变](skybox.md)（单时刻天空盒）；[太阳从早走到晚，物体当场变色](frame_lighting.md)（只看光照总线）。
