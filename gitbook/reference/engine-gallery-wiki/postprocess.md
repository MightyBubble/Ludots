# 四根调色推子一起推

世界先画进 RenderTexture，再由 `RaylibPostProcessRenderer` 调色出场：曝光、对比、饱和、暗角四根推子以不同频率正弦摆动，HUD 实时报数。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_postprocess/poster.png" src="artifacts/evidence/engine_raylib_postprocess/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_postprocess/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `postprocess` |
| preset | `engine_raylib_postprocess` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/PostProcessScene.cs` |
| 承接渲染器 | `RaylibPostProcessRenderer`（`postprocess` 着色器） |
| 注册表条目 | `engine_raylib_postprocess`（`showcase.registry.json`，tier T1） |

调色参数走 `RaylibPostProcessConfig`（Exposure/Contrast/Saturation/VignetteStrength），场景演示的是随时间调制；宿主/作者侧就是同一配置对象——`BeginWorldFrame(…, config)` 进、`EndWorldFrame(…, config)` 出，两调用之间画世界即可。没有第二个后处理入口，也不接受 shader 级散改。

## 这场演的是什么

- 八个渐变色立方带金球绕行，作为调色的「标准色卡」；中央灰基座收暗角。
- 四轴调制范围：曝光 0.78–1.38、对比 0.85–1.40、饱和 0.25–1.60、暗角 0.05–0.43（`PostProcessScene.Draw`），频率各异、永不同步。
- HUD 逐帧打印四参数当前值，可与画面肉眼对账。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/postprocess.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 1.19 | 1.03 | 58.63 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_postprocess --adapter raylib
```

## 边界与深读

- 本车道只有调色四轴；泛光（bloom）、景深不在词汇里，出现即越权。
- 深读：[Raylib 最小引擎能力总览](../../architecture/raylib-engine-capabilities.md)的「渲染车道矩阵」。
