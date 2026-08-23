# 十二具骨架，各自走路

十二具 mannequin 围成一圈，每具的行走相位各不相同——`RaylibSkinnedPlayback` 逐实例解算 clip 帧相位并上传骨骼姿态，是非合批 GPU 蒙皮路径的最小完整演示。

<img src="artifacts/acceptance/engine_gallery_all/gpu_skinning.png" alt="gpu_skinning 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `gpu_skinning` |
| preset | `engine_raylib_gpu_skinning` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/GpuSkinningScene.cs` |
| 承接渲染器 | `RaylibGpuSkinnedModelCache` + `RaylibSkinnedPlayback`（骨骼装载 fail-loud） |
| 注册表条目 | `engine_raylib_gpu_skinning`（`showcase.registry.json`，tier T1） |

模型资产一行 `MeshAssetDescriptor.Model(id, "Models/mannequin_large_walk.glb")`——骨骼/动画结构装载期校验，缺件即抛（无静默假蒙皮）。每实例相位是打包状态 `AnimatorPackedState`（clip + 归一化时间），由回放器解析成帧号上传。宿主侧同一套缓存喂合批车道；离线重定向与 GPU 实例化蒙皮的完整链路见 [GPU Skinned Instancing 与离线重定向](../../architecture/gpu-skinned-instancing-and-offline-retarget.md)。

## 这场演的是什么

- 12 实例环形排布（半径 9.5m），相位按索引错开（速率 0.55），同 clip 不同进度。
- HUD 报告实例数与 clip 帧数；每具骨骼独立解算——这条路径实例数少但相位完全自由。
- 与 [四千个兵环形行军](crowd_anim.md) 成对：本场景逐实例，那一场按相位分桶合批。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/gpu_skinning.png` / `.json`（CI 批跑，30 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 30 | 12.22 | 14.44 | 16.39 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_gpu_skinning --adapter raylib
```

## 边界与深读

- 逐实例上传骨骼姿态是 O(实例数) 的；大人群请走合批车道（相位分桶）。
- 深读：[GPU Skinned Instancing 与离线重定向](../../architecture/gpu-skinned-instancing-and-offline-retarget.md)。
