# Scenario: skia-gpu-ui-compositor — Raylib 帧路径 Skia 全 GPU 化验收

## Header

- scenario: skia_gpu_ui_compositor
- build: worktree `C:\001_AI\LudotsProd-skia-gpu`，分支 `skia-gpu-ui-compositor`
- base SHA: `23deddf07f`（origin/main）
- head SHA: 见 PR #1613 最新提交
- clock: 2026-09-21（本地）
- adapter: raylib（真实 GL 窗口，Windows 本机 GPU）

## Scenario Card

- player goal: 三层 Skia（UnderUi HUD / UI 面板 / TopMost）默认全部 GPU 驱动，CPU 光栅只剩 kill-switch 显式回退；跨引擎合同文档可指导 Unity/Unreal 适配。
- action: 默认配置与 `LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI=1` 两种配置各跑真实 GL 场景；跑源合同测试与像素等价测试；跑基准矩阵。
- success: 两种配置各自产出正确视觉（HUD 位于顶部、面板底色/标题白字/罗盘橙色环命中），GPU 失败路径抛异常不静默回退，基线红清单外的测试全绿。
- guard branches: GPU 表面创建失败 → 一次性 Warn 后 production 路径抛 `InvalidOperationException`；kill-switch 全开 → 整链光栅可跑。

## UAT

- Given main 上 UI 面板层走 `SkiaRasterLayer` + `Raylib.UpdateTexture` 整窗上传、`SkiaUiRenderer` 每次渲染建整窗 CPU 中间面
- When 合成器 UI 层改为 `RaylibSkiaGpuCanvasSurface` 直渲（`SetTarget` + `IsDirty` 脏门控）且节点模糊改 `SaveLayer`
- Then 默认路径不再出现整窗光栅上传（`UpdateTexture` 只剩回退分支），直渲与 canvas 路径逐像素相等，gallery 场景 GPU 路径 HUD 位置与内容正确，kill-switch 回退路径视觉不变

## Timeline

- [T+001][build] 全解决方案构建通过（0 error）
- [T+002][unit] `SkiaUiRendererSurfaceTargetTests` 4/4：直渲 vs canvas 逐像素相等（differing=0）、backdrop blur 目标面采样硬边、filter blur 软溢出
- [T+003][unit] 源合同测试 3/3：GPU UI 默认开 + kill-switch 命名 + 终绘顺序 underlay→UI→topmost + production 抛错 + BottomLeft 取向
- [T+004][unit] UiShowcaseTests 93/93、RaylibAdapterTests 312/312、ArchitectureTests 全绿
- [T+005][realgl] gallery skia_overlay GPU 首跑：HUD 整幅翻至屏幕底部 → 定位 `GRSurfaceOrigin.TopLeft` 与宿主负高度翻转叠加镜像（main 潜伏缺陷，默认配置优先 framebuffer 直写未被走到）
- [T+006][fix] `RaylibSkiaGpuCanvasSurface` 改 `GRSurfaceOrigin.BottomLeft`；合同测试钉住
- [T+007][realgl] gallery GPU 重跑：面板底色 (52,52,55)、标题白字 (255,255,255)、罗盘环 (254,200,91) 全部命中，底部干净（`gallery_gpu_default_f0200.png`）
- [T+008][realgl] gallery 回退（kill-switch）跑：同位置面板色 (49,49,50)，视觉与 GPU 路径一致（`gallery_raster_fallback_f0200.png`）
- [T+009][realgl] 官方证据重录：`artifacts/evidence/engine_raylib_skia_overlay/{play.mp4,poster.png}`（GPU 路径，poster 像素验证通过）
- [T+010][realgl] 游戏进程真跑：blacksmith preset 75s+ 稳定运行（新合成器 + GPU UI 每帧参与，无异常退出）
- [T+011][bench] 420 帧基准矩阵：GPU avg 0.554ms / p95 0.664ms；光栅回退 avg 3.614ms / p95 4.749ms

## Outcome

- success: yes
- reason: `gpu_pixel_verified`、`fallback_pixel_parity`、`contract_tests_green`、`baseline_red_untouched`
- 修复的潜伏缺陷：render-texture 表面取向镜像（缺陷来自 main 的 `RaylibSkiaGpuOverlaySurface`，非默认配置不触发，故未在 main 上暴露）

## Summary Stats

| 套件 | 结果 | 备注 |
|---|---|---|
| PresentationTests（合同切片） | 3/3 通过 | 新增 `RaylibOverlayCompositor_RendersUiLayerThroughGpuSurfaceByDefault`，CI 白名单收录 |
| UiShowcaseTests | 93/93 通过 | 含新增 4 个直渲等价测试 |
| RaylibAdapterTests | 312/312 通过 | raylib-field 切片 46/46 |
| ArchitectureTests | 全部通过 | — |
| PresentationTests 全量（干净终跑） | 12 失败 / 1155 通过 / 1167 总 | 12 个失败全部在基线 13 红清单内（对照 `baseline-red-main.md`），**零新增失败**；基线中的 `Benchmark_SkiaOverlay_10kHudAndText_Writes120HzReport` 本次转绿（时敏基准断言）；新增合同测试通过（+1 总数来源） |

| 基准（skia_overlay，420 帧，本机） | avg ms | p95 ms | wall ms |
|---|---|---|---|
| GPU render-texture（默认） | 0.554 | 0.664 | 288.3 |
| 光栅回退（kill-switch） | 3.614 | 4.749 | 1594.9 |

## Evidence Artifacts

- `gallery_gpu_default_f0200.png` / `gallery_raster_fallback_f0200.png`：真机 GPU/回退对照截图（像素断言见 Timeline T+007/T+008）
- `gpu-default/`：entity_command_panel preset 启动计划录证（launcher-recorder-artifacts 模式）
- `artifacts/benchmarks/skia-gpu-ui-compositor/*.json`：基准原始数据
- `artifacts/evidence/engine_raylib_skia_overlay/{play.mp4,poster.png}`：官方 showcase 证据（GPU 路径重录）
