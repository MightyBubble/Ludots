# 图元阵与原型的动效基线

`RaylibPrimitiveRenderer` 直接模式的最小完整演示：48 个彩色图元随时间波动，坦克/人形原型走 `AnimatorPackedState` 驱动的 locomotion/aim 通道动效——所有「纯数据画东西」的车道都从这条基线出发。

<img src="artifacts/acceptance/engine_gallery_all/primitives.png" alt="primitives 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `primitives` |
| preset | `engine_raylib_primitives` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/PrimitivesScene.cs` |
| 承接渲染器 | `RaylibPrimitiveRenderer`（`PrimitiveDrawItem` 族直接消费） |
| 注册表条目 | `engine_raylib_primitives`（`showcase.registry.json`，tier T1） |

绘制条目是纯数据（`PrimitiveDrawItem`：资产 id、材质、位姿、颜色、`Animator` 打包状态），渲染器按资产/材质分桶合批。宿主里这条车道承接 Presenter 投影下来的群体请求；动画通道（locomotion/aim）由 `AnimatorPackedState` 的 controller/normalized time/flags 描述，见 `GalleryAnimationChannels.Register` 的注册写法。

## 这场演的是什么

- 48 图元阵列（`PrimitivesScene`）随正弦波动——基线吞吐与分桶行为直接可见。
- 人形原型走 `AnimatorPackedState.Create(controllerId: 1)` + `SetNormalizedTime01` 循环推进 locomotion；同一条目换通道就是 aim——动画语义全部在数据里。
- 图元阵进阴影深度 pass，与光照/阴影车道共用总线。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/primitives.png` / `.json`（CI 批跑，30 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 30 | 2.08 | 2.05 | 25.15 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_primitives --adapter raylib
```

## 边界与深读

- 直接模式逐帧喂快照；静态大阵的 ISM 吞吐对照见 [三万个方块球，一次合批画完](instancing.md)。
- 深读：[Raylib 最小引擎能力总览](../../architecture/raylib-engine-capabilities.md)的「渲染车道矩阵」。
