# 给世界画辅助线

网格线、脉动单位圈、选中盒、由相机推得的视锥线——`DebugDrawCommandBuffer` 手工填充命令，`RaylibDebugDrawRenderer` 一次消费，HUD 报告当帧线/圆/盒数量。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_debug_draw/poster.png" src="artifacts/evidence/engine_raylib_debug_draw/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_debug_draw/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `debug_draw` |
| preset | `engine_raylib_debug_draw` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/DebugDrawScene.cs` |
| 承接渲染器 | `RaylibDebugDrawRenderer` + `DebugDrawCommandBuffer`（命令缓冲） |
| 注册表条目 | `engine_raylib_debug_draw`（`showcase.registry.json`，tier T1） |

调试绘制是命令缓冲不是散调用：往 `DebugDrawCommandBuffer` 塞 `DebugDrawLine2D`/`DebugDrawCircle2D`/盒命令（平面坐标 + 颜色），渲染器统一落平面（`PlaneY`）绘制，圆的段数（`CircleSegments = 40`）是渲染器参数。宿主/工具侧同一缓冲合同——先攒命令后绘制，没有逐条立即模式的 API。

## 这场演的是什么

- 11×11 主网格线加红/蓝坐标轴；三色单位圈（青/黄/绿）半径按不同频率脉动。
- 视锥线由相机参数当场推得——移动相机，视锥随之更新。
- 两个示例立方投真实方向光阴影，与调试线同框对照「游戏物 vs 辅助物」。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/debug_draw.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 1.02 | 0.80 | 55.23 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_debug_draw --adapter raylib
```

## 边界与深读

- 调试线不参与光照/阴影/深度遮挡（覆盖语义），永远画在最上层的世界平面。
- 深读：[Raylib 最小引擎能力总览](../../architecture/raylib-engine-capabilities.md)的「渲染车道矩阵」。
