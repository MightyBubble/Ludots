# 太阳从早走到晚，物体当场变色

光照总线 `RaylibFrameLighting` 的相位从清晨推到黄昏再推回来，十个绕行的彩色图元、天空、阴影、环境光全部当场跟着变。

<video controls playsinline preload="metadata" poster="artifacts/evidence/engine_raylib_frame_lighting/poster.png" src="artifacts/evidence/engine_raylib_frame_lighting/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/engine_raylib_frame_lighting/play.mp4`。
</video>

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `frame_lighting` |
| preset | `engine_raylib_frame_lighting` |
| 场景源码 | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/FrameLightingScene.cs` |
| 承接渲染器 | `RaylibFrameLighting`（光照总线）+ `RaylibPrimitiveRenderer`（Immediate 车道消费） |
| 注册表条目 | `engine_raylib_frame_lighting`（`showcase.registry.json`，tier T1） |

`RaylibFrameLighting` 从默认环境 JSON 装载（`LoadFromDefaultPath`），一条总线携带光向/环境/光色/强度/雾/视点；mod 侧的昼夜只改相位——环境配置树里的天空、雾、阴影参数随相位联动，作者不用逐个 shader 调参。数据驱动昼夜的合同见[能力总览](../../architecture/raylib-engine-capabilities.md)。

## 这场演的是什么

- 相位按正弦在 0.24–0.74 间摆动（`FrameLightingScene.Draw`），HUD 实时打印 `day phase` / `sun Y` / `ambient W`。
- 十个图元绕中心环行，方块与球交替；颜色随相位一起被重新照亮。
- 太阳方向画成黄色十字标记（`sun × 60` 处），看得见「光从哪来」与阴影方向的对应关系。
- 图元走 Immediate 直接模式快照（`GalleryPrimitiveSnapshot`），同一条车道也承载阴影深度 pass。

## 验收证据

截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/frame_lighting.png` / `.json`（120 帧验收批）：

| frames | avg ms | p95 ms | max ms |
|---|---|---|---|
| 120 | 5.31 | 22.34 | 414.03 |

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_frame_lighting --adapter raylib
```

## 边界与深读

- 本场只演示总线驱动；天空渐变烘焙的完整昼夜循环见 [四十八秒过完一整天](sky_daynight.md)。
- 深读：[渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)。
