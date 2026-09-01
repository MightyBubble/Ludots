# 四千个兵环形行军

4,096 具 mannequin 排成 14 环行军——CPU 每帧只重算环位/朝向并打包动画相位，骨骼蒙皮全部走 `skinning_instanced` 合批车道，不存在 CPU 变换假蒙皮回退。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_crowd_anim/poster.png" src="artifacts/evidence/engine_raylib_crowd_anim/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_crowd_anim/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `crowd_anim` |
| preset | `engine_raylib_crowd_anim` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/CrowdAnimScene.cs` |
| 承接渲染器 | `RaylibPrimitiveRenderer` 的 `GpuSkinnedInstance` 车道（`skinning_instanced` 着色器） |
| 注册表条目 | `engine_raylib_crowd_anim`（`showcase.registry.json`，tier T1） |

合批的关键是离散化取舍（`CrowdAnimScene` 源注释）：行走 clip 62 帧，相位取 16 桶——比 16 桶更细的相位差肉眼不可辨；环带色量化为 7 档。7 色 × 16 相位 = 112 逻辑桶，mannequin 6 网格 → 每帧 672 次 `DrawMeshInstanced`，每桶一次 `UpdateModelAnimationBones`。桶数每 +1 就多 6 次 uniform 上传与一次骨骼姿态计算，是本车道主要帧耗来源。

## 这场演的是什么

- 4,096 实例（`TargetInstances`）14 环环形行军，环带色 7 档分层渐变保留。
- 相位分桶后整队走路依然连贯——离散化在人群尺度下的视觉等价性本身就是演示点。
- 相位 0.38 的固定日光下整队投方向光阴影（`shadow_depth_skinning_instanced`）。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_raylib_crowd_anim/screen.png` / `stats.json`（重场景独立批，120 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 20.29 | 20.91 | 361.27 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_crowd_anim --adapter raylib
```

## 边界与深读

- 少量实例且相位必须完全自由时走逐实例路径：[十二具骨架，各自走路](gpu_skinning.md)。
- 深读：[GPU Skinned Instancing 与离线重定向](../../architecture/gpu-skinned-instancing-and-offline-retarget.md)。
