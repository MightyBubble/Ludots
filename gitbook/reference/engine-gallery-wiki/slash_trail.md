# 挥砍的刀光弧线

脚本化水平挥砍驱动 `TrailMeshBuffer`——挥砍窗口内逐帧记录刀刃 base/tip 世界坐标，按寿命淘汰并折算 age01，`RaylibTrailMeshRenderer` 把样本条带重建为三角带，顶点色沿轨迹渐隐。头插/寿命淘汰/age01 折算复用共享纯工具 `TrailSampleHistory`（Core 的 `TrailMeshRuntime` 同一实现），画廊不持有第二套采样语义。

## 作者写法

| 项 | 值 |
|----|----|
| scene id | `slash_trail` |
| preset | `engine_raylib_slash_trail` |
| 场景源码 | `src/Content/Ludots.Content.EngineGallery/Scenes/SlashTrailScene.cs` |
| 承接渲染器 | `RaylibTrailMeshRenderer` + `TrailMeshGeometry` |
| 注册表条目 | `engine_raylib_slash_trail`（`showcase.registry.json`，tier T1） |

轨迹的采样语义（头插最新样本、超寿命尾部淘汰、`age01 = clamp((now − t)/lifetime, 0, 1)` 渐隐折算）全部落在 `Ludots.Platform.Abstractions` 的 `TrailSampleHistory`，Core 的 `TrailMeshRuntime`（`PresenterBehaviorSystem` 唯一写入方）与画廊场景共用同一实现；渲染走 `Ludots.Raylib.Render` 的 `RaylibTrailMeshRenderer`，与宿主同一实现。场景时间用固定 1/60 步进而非真实帧间隔：headless 截图帧（第 120 帧）稳定落在挥砍末段，验收可复现。

## 这场演的是什么

- 刀光刀柄绕轴摆出一个水平挥砍弧（ease-out 三次缓动），刀刃即时画线、轨迹滞后成弧。
- 轨迹条带按寿命淡出：head 高亮、tail 透明，顶点色在 `headColor`/`tailColor` 间按 `age01` 线性插值。
- 挥砍窗口结束后存量样本自然老化离场，缓冲回到空态，下一刀从头再来。

## 验收证据

视觉验收证据（`screen.png` + `stats.json` 到 `artifacts/acceptance/engine_raylib_slash_trail/`，录像到 `artifacts/evidence/engine_raylib_slash_trail/`）由标准验收 CLI 与 `scripts/record-engine-galleries.py` 实跑生成；本页在实跑采样前不填造帧统计。真实运行采样是证据的硬性要求，见 [引擎画廊开发指南](../../architecture/raylib-engine-gallery-dev-guide.md)。

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_slash_trail --adapter raylib
```

## 边界与深读

- 轨迹是「演示层」渲染：`TrailMeshBuffer` 的完整行为链路（behavior 激活/停用、`presentation.trailMeshCapacity` 固定容量 fail-fast、stableId 唯一性）见 [Presenter 编译车道](../../architecture/presenter-compiled-lanes.md) 的 TrailMesh 行。
- 样条带覆盖（路径/河流示意，贴地语义）见 [地面上的圈与飘带](ribbon_overlay.md)；本场景是自由空间 mesh 拖尾，不贴地。
