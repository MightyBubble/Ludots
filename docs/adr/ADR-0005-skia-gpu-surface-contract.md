# ADR-0005 Skia GPU 表面合同：Raylib 帧路径全 GPU 化与跨引擎适配接缝

## 1 背景

Raylib 宿主的 Skia 渲染分两条腿：HUD 覆盖层（UnderUi / TopMost 的 `PresentationOverlayScene` 批渲染）已经 GPU 化——`RaylibSkiaGpuOverlaySurface` 走 render-texture，`RaylibSkiaFramebufferOverlaySurface` 直写默认帧缓冲；但 UI 面板层（`UIRoot` 挂载的 retained 面板）仍在 CPU 光栅上：`SkiaRasterLayer` 画整窗位图，`RaylibSkiaRenderer.UpdateTexture` 每次脏刷新做整窗 CPU→GPU 上传，且 `SkiaUiRenderer.RenderToCanvas` 每次渲染都额外建一块整窗 CPU 中间面供 backdrop blur 回读。

与此同时，Unity / UE5 等下游宿主适配按 `docs/architecture/adapter_pattern.md` §7 归属下游仓库维护，但 GPU 绑定的做法只存在于 `Adapter.Raylib` 的 internal 类和验收审计的散文里，没有正式合同可对照。

## 2 决策

*   Raylib 帧路径上的 Skia 全部 GPU 驱动，默认开启：UI 面板层渲染进 GPU render-texture 表面（`GRContext` 绑定宿主 OpenGL 上下文），沿用 `UIRoot.IsDirty` 脏门控缓存纹理。
*   CPU 光栅路径保留为显式配置的回退，收进环境变量 kill-switch；production 路径 GPU 渲染失败直接抛异常，不静默回退。Linux 云端验收继续用 kill-switch 跑全光栅。
*   四个适配接缝定为跨引擎正式合同：① 宿主图形上下文上创建 `GRGlInterface` + `GRContext`；② 宿主 render target 包装 `GRBackendRenderTarget` 生成 `SKSurface`；③ 分层合成顺序 UnderUi → UI 面板 → TopMost；④ 策略姿态（默认 GPU 开、`LUDOTS_<HOST>_DISABLE_SKIA_GPU_*` 命名、失败即抛）。合同正文落在 `gitbook/architecture/skia-gpu-overlay-adapter-guide.md`。
*   代码级不变量：宿主与 Skia 共享图形状态，每批 GPU 渲染前必须 `ResetContext`；`Flush(submit:true)` + `Submit` 之后宿主才能消费该纹理；render-texture 消费用 premultiplied alpha 且 Y 翻转，默认帧缓冲表面用 FBO 0 + `GRSurfaceOrigin.BottomLeft`。
*   retained UI 用 render-texture + 脏门控复用纹理；每帧全变的 HUD 才用默认帧缓冲直写。
*   GPU render-texture 表面与 GL 上下文工厂提升为 `Ludots.Raylib.Render` 公共基建，作为合同的参考实现；`Adapter.Raylib` 只保留宿主接线。

## 3 备选方案

*   UI 面板层继续 CPU 光栅 + 整窗上传：拒绝。每次脏刷新都要付整窗上传成本，`RenderToCanvas` 的中间光栅面把成本再翻一倍，与已 GPU 化的 HUD 层分层合同不匹配。
*   UI 面板层默认帧缓冲直写：暂不采用。retained UI 受益于脏门控的纹理复用；直写意味着每帧全量重渲。
*   删除 CPU 光栅回退：拒绝。Linux 云端验收依赖 kill-switch 强制光栅（见 `docs/audits/raylib-visual-atmosphere/ACCEPTANCE.md`）。
*   把四个接缝提升为 Core port 接口：暂不采用。商业引擎适配归下游仓库；Core 侧先以合同文档 + 参考实现约束，等第二个引擎适配真实落地再评估是否上升为 port。

## 4 影响

*   `Ludots.Raylib.Render` 新增公共 GPU 表面基建；`Ludots.Adapter.Raylib` 的 HUD 表面变为薄包装。
*   `Ludots.UI.Skia` 的 `SkiaUiRenderer` 支持直渲目标表面（backdrop blur 直接在目标面上 Snapshot，GPU 面无 CPU 回读）；节点 filter blur 改 `SaveLayer + ImageFilter`，删除区域离屏光栅面。
*   新增环境变量 `LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI`。
*   正式合同写入 `gitbook/architecture/skia-gpu-overlay-adapter-guide.md`；`gitbook/architecture/ui-rendering-and-surface-ownership.md` 的分层口径同步更新。
*   离线证据工具与 headless 测试继续 CPU 光栅，不属于本合同范围。

## 5 后续约束

*   production 路径不得静默回退 CPU 光栅；回退只能由环境变量显式配置。
*   宿主消费 Skia 渲染结果前必须完成 `Flush` + `Submit`；每批 GPU 渲染前必须 `ResetContext`。这两条对任何新宿主适配同样成立。
*   引擎无关层（`SkiaOverlayRenderer`、`Ludots.UI.Skia` 控件、`PresentationOverlayScene`）不得引入宿主特定依赖。
*   新宿主适配的 kill-switch 必须沿用 `LUDOTS_<HOST>_DISABLE_SKIA_GPU_*` 命名，并在适配指南登记。
