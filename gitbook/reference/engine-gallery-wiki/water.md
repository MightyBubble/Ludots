# 水面上下各画一遍，合成一片海

海床在反射、折射两个 RenderTexture 里各渲染一次，主画面 `water.fs` 采样两张 RT 加 DUDV 扭曲——近处水纹晃动，远处反射天空。

<img src="artifacts/acceptance/engine_gallery_all/water.png" alt="water 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `water` |
| preset | `engine_raylib_water` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/WaterScene.cs` |
| 承接渲染器 | `RaylibWaterPass`（反射/折射双通道）+ `water` 着色器 |
| 注册表条目 | `engine_raylib_water`（`showcase.registry.json`，tier T1） |

水面是数据驱动配置：场景手工构造 `gallery.ocean` 条目演示作者写法——`waterPlaneY`、`resolutionScale: 0.5`、`waveStrength`、`moveSpeed`、`dudvUri`。宿主/作者在环境配置 JSON 写同样条目；DUDV 扭曲图本场景程序化生成（256²），线上资产换成正式贴图即可。

## 这场演的是什么

- 每帧三步：反射相机（`BuildReflectionCamera`，水面镜像）画一遍世界 → 折射 pass 再画一遍 → 主 pass 地形渲染器 `BindReflectiveWater` 挂上两张 RT 由 `water.fs` 合成。
- 24×24 chunk 海床（12m 间距）起伏透过折射可见；天空渐变映在反射里。
- 波纹强度/流速走配置（0.035/0.05），不是 shader 魔法数。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/water.png` / `.json`（CI 批跑，30 帧）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 30 | 6.01 | 4.43 | 93.05 |

（世界画三遍是本车道固有成本，数值供回归比对。）

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_water --adapter raylib
```

## 边界与深读

- 湖面/局部水体走地表着色车道的 `emitWater`，不进反射通道；见 [一座岛的皮肤是顶点色刷的](terrain_surface.md)。
- 深读：[Raylib 最小引擎能力总览](../../architecture/raylib-engine-capabilities.md)的「渲染车道矩阵」。
