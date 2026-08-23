# 地面上的圈与飘带

环形覆盖直接画在地面上，样条带沿路径铺开——`GroundOverlayBuffer` 与 `SplineRibbonBuffer` 手工填充，绘制统一走 `RaylibWorldOverlayRenderer`，宿主与画廊共用同一实现。

<img src="artifacts/acceptance/engine_gallery_all/ribbon_overlay.png" alt="ribbon_overlay 验收截图" width="880">

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `ribbon_overlay` |
| preset | `engine_raylib_ribbon_overlay` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/RibbonOverlayScene.cs` |
| 承接渲染器 | `RaylibWorldOverlayRenderer`（绘制核唯一实现） |
| 注册表条目 | `engine_raylib_ribbon_overlay`（`showcase.registry.json`，tier T1） |

世界覆盖层是「贴地语义」：环形/扇形覆盖（`GroundOverlayShape.Ring`，技能指示圈、范围标记）与样条带（路径、传送带、河流示意）两个缓冲各自填充，一次绘制调用消费。宿主侧同一渲染器接 Core 的覆盖请求；「绘制核唯一」意味着画廊里看到的画质/行为与游戏内一致，不存在两套覆盖层实现。

## 这场演的是什么

- 环形覆盖（Ring 形状）绕中心铺在地表，样条带沿手工控制点延伸。
- 两根道具柱投出方向光阴影——覆盖层「贴地不遮光」，不参与深度遮挡。
- 覆盖色与半透明在地面网格上方直接合成，无 z-fighting（贴地偏移由渲染器内建）。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/ribbon_overlay.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 0.56 | 0.52 | 25.16 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_ribbon_overlay --adapter raylib
```

## 边界与深读

- 世界覆盖层是贴地 2.5D 语义；真实水体（反射/折射）见 [水面上下各画一遍，合成一片海](water.md)。
- 覆盖层与投影贴花的分工：贴花「投」到接收面（跟随地形法向），覆盖层「铺」在世界固定平面。
