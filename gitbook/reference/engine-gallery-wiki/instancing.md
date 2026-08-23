# 三万个方块球，一次合批画完

300×100 的棋盘格立方/球阵共 30,000 个实例，正弦波浪起伏——`RaylibBenchmarkRenderer` 用纯数据驱动 ISM 合批，HUD 实时报可见数、分桶数与 CPU 绘制耗时。

<img src="artifacts/acceptance/engine_gallery_all/instancing.png" alt="instancing 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `instancing` |
| preset | `engine_raylib_instancing` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/InstancingScene.cs` |
| 承接渲染器 | `RaylibPrimitiveRenderer`（`instancing` 着色器）经 `IRaylibBenchmarkRenderer` 直驱 |
| 注册表条目 | `engine_raylib_instancing`（`showcase.registry.json`，tier T1） |

实例是 `RaylibBenchmarkInstance` 结构体数组（mesh 资产 id、材质 id、位姿、颜色），一次 `SetScene` 全量灌入；改动画就是改数组。宿主侧同一车道承接静态网格群（植被、道具群），作者面走图元快照/资产行，不手写 draw。分层上这属于「平台基准」用法（绕过 Presenter 直驱渲染器测吞吐），边界见[能力标准化 Showcase](../../architecture/engine-capability-showcases.md)。

## 这场演的是什么

- 30k 实例棋盘格交替立方/球，六色调色板按 50 格分块；高度与缩放按正弦波浪联动。
- 相机固定在 (0, 120, 210)、fov 55° 的高位俯瞰——一次看全吞吐规模。
- 整阵进方向光阴影深度 pass（`shadow_depth_instanced`）；地面接收影。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/instancing.png` / `.json`（CI 批跑，30 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 30 | 10.32 | 12.69 | 60.78 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_instancing --adapter raylib
```

## 边界与深读

- 本车道是静态实例化（ISM）；带骨骼动画的实例化合批见 [四千个兵环形行军](crowd_anim.md)。
- 合批外部源合同（宿主如何喂实例数据）：[Instanced Batch 外部 Source Contract](../../architecture/instanced-batch-source-contract.md)。
