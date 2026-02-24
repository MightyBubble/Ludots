---
文档类型: 架构设计
创建日期: 2026-02-10
最后更新: 2026-02-10
维护人: X28技术团队
文档版本: v0.1
适用范围: 交互层 - UI系统 - Skia UI 跨引擎渲染方案 A
状态: 草案
---

# Skia UI 跨引擎渲染方案 A（贴图合成）

# 1 背景与问题定义

UI 系统希望做到“逻辑与控件树复用”，同时能运行在多个宿主引擎（Raylib/Unity/Unreal/Web）之上。不同引擎的原生 UI 框架差异巨大，强行对齐到“统一 UI 组件模型”会引入大量适配与功能缺口。

因此采取“方案 A”：

- UI 统一使用 Skia 绘制到离屏 Surface（CPU 或 GPU）。
- 引擎侧只负责把该 Surface 的输出作为一张纹理/贴图呈现到屏幕。

本方案的关键在于把跨引擎差异收敛到“Present 层”（像素上传/纹理更新/显示），而不是把差异扩散到控件树与布局系统。

# 2 设计目标与非目标

目标：

- UI 逻辑与控件树跨引擎复用：UI 绘制入口稳定，平台仅替换 “SkiaSurface + Present”。
- 性能可控：允许 0GC/低分配，支持分辨率缩放与 Dirty Rect（可选）。
- 行为一致：在相同输入与资源下，UI 布局/绘制结果跨平台一致（差异必须显式记录）。

非目标：

- 不追求“跨引擎 0-copy 到 GPU”：引擎内部纹理更新与 GPU buffer 管线会发生拷贝，本方案只保证托管侧 0GC 与可控的跨边界拷贝次数。
- 不在方案 A 中对齐各引擎原生 UI（如 UMG/Unity UI）：那属于“DrawList/抽象渲染接口”的另一方案。

# 3 核心设计

## 3.1 SSOT 与边界

UI 作为表现层的一部分，其 SSOT 为“UI 树 + 输入状态 + 资源映射”。渲染后端不允许隐式修改 UI 逻辑状态。

代码入口（当前实现形态）：

- UI 顶层渲染入口：`src/Libraries/Ludots.UI/UIRoot.cs`
- 控件树渲染入口：`src/Libraries/Ludots.UI/Widgets/Widget.cs`

当前宿主示例（Raylib）：

- Skia Surface + Present：`src/Client/Ludots.Client.Raylib/Rendering/RaylibSkiaRenderer.cs`
- 主循环调用点：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`

## 3.2 模块划分与职责

本方案的模块边界建议如下：

- UI 运行时（跨平台，稳定）
  - 输入：读入 UI 输入状态（鼠标/键盘/触摸的“语义化状态”）
  - 布局：计算控件布局与裁剪
  - 绘制：把 UI 绘制到抽象的 Skia 画布入口（当前为 `SKCanvas`）
- 平台 SkiaSurface（平台相关，可替换）
  - 分配/复用 Surface 与像素缓冲（CPU raster）或 GPU surface（GPU backend）
  - 提供每帧可用的画布对象（`SKCanvas` 或等价）
- 平台 Present（平台相关，可替换）
  - CPU raster：把 RGBA 像素上传/更新到引擎纹理
  - GPU backend：把 GPU texture handle 绑定/拷贝到引擎纹理（视引擎接口）
  - 显示：在合适的渲染阶段将 UI 贴到屏幕（Overlay/全屏 Quad/后处理）

## 3.3 数据流（每帧）

```
Input Backend
  -> UI Input State
     -> UI Layout (Widget Tree)
        -> UIRoot.Render(SKCanvas)
           -> Skia Surface (offscreen)
              -> Present (upload/bind)
                 -> Engine draws a textured quad/overlay
```

建议以“帧级接口”描述平台层契约（伪接口，仅作为文档口径）：

- BeginFrame(width, height, dpiScale) -> Canvas
- EndFrame(dirtyRect?) -> Present

## 3.4 线程模型与生命周期

推荐默认线程模型（保守、易落地）：

- UI Render（Skia 绘制）：在引擎渲染线程或主线程执行（可避免资源跨线程问题）。
- Present（纹理更新）：必须在引擎允许更新纹理的线程执行（Unity 通常为主线程；Unreal 需要走引擎推荐的 RHI 更新路径）。

优化模型（可选）：

- CPU raster 场景：UI Render 可迁移到工作线程，输出像素缓冲；Present 仍在主线程完成上传。
- GPU backend 场景：若引擎允许共享上下文/纹理句柄，可减少 CPU 参与，但实现复杂度显著上升。

生命周期：

- Surface/Texture 必须可复用；只在分辨率或像素格式变化时重建。
- UI 资源（字体/图片）应由资源注册表统一管理；缺失映射必须 fail-fast。

## 3.5 内存与 0GC 口径

目标是“托管侧 0GC / 低分配”，主要约束如下：

- `SKSurface` 与像素缓冲长期复用，不允许每帧 `new byte[]`。
- Dirty Rect 计算与命令分发避免 LINQ 与临时集合；必要时用预分配 `Span`/数组池。
- UI 文本/路径绘制中避免反复创建 `SKPaint/SKPath`；使用缓存或对象池（缓存失效规则需明确）。

注意：

- 引擎纹理上传本质会发生拷贝；本方案不承诺“从 UI 到 GPU 的 0-copy”。

## 3.6 Dirty Rect（可选增强）

Dirty Rect 的目标是减少纹理更新成本。建议分两层实现：

1. UI 层：提供可选的“脏区域合并器”，把控件树变化归并为 0~N 个矩形。
2. 平台层：若引擎纹理更新 API 支持局部更新，则按矩形更新；否则退化为全量上传。

验收口径：

- Dirty Rect 开启时，在小改动（例如光标闪烁、局部文本变化）下显著减少上传带宽。
- Dirty Rect 关闭时行为必须与开启一致（仅性能差异）。

## 3.7 字体、DPI 与像素口径

跨引擎一致性依赖明确的像素口径：

- UI 逻辑单位：建议以“逻辑像素”定义，最终由 `dpiScale` 映射到实际像素。
- 字体：必须通过资源注册表统一加载同一份字体文件，避免平台字体回退导致布局差异。
- 纹理格式：统一为 `RGBA8888`（或由平台明确约束），并在 Present 层处理字节序差异。

## 3.8 输入对齐

方案 A 的输入对齐建议：

- 引擎输入统一归一化到 UI 坐标：屏幕像素 -> UI 逻辑坐标（考虑 dpiScale 与 letterbox）。
- 鼠标滚轮/触摸手势以“语义事件”进入 UI（Scroll/Drag/Click），避免平台直接注入原始设备数据导致不一致。

# 4 平台落地指南（最小实现清单）

## 4.1 Raylib（现有参考实现）

参考：

- `src/Client/Ludots.Client.Raylib/Rendering/RaylibSkiaRenderer.cs`
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`

落地点：

- CPU raster：Skia 画到 `SKSurface` 的像素，`UpdateTexture` 上传，再 `DrawTexture` 覆盖屏幕。

## 4.2 Unity

建议落地形态：

- CPU raster：Skia 输出 RGBA buffer -> `Texture2D` 更新 -> `RawImage`/全屏 Quad 显示。
- Surface 复用：分辨率变化时重建 `Texture2D` 与 `SKSurface`。

线程建议：

- Skia 绘制可放工作线程（可选），纹理更新必须回主线程。

## 4.3 Unreal

建议落地形态：

- CPU raster：Skia 输出 RGBA buffer -> 动态纹理更新（RHI/动态纹理资源）-> UMG/全屏材质显示。
- 大规模 UI：优先使用局部更新或降低 UI 渲染分辨率（再由引擎放大）。

线程建议：

- 纹理更新遵循引擎推荐路径；不直接在非安全线程触碰资源对象。

## 4.4 Web（如果需要）

说明：

- 方案 A 在 Web 通常意味着：.NET 侧生成像素 -> JS/Canvas/WebGL 贴图呈现。
- 若未来引入 CanvasKit，则属于另一方案（GPU backend），需要单独定义后端边界。

# 5 风险与迁移策略

风险：

- CPU raster 在高分辨率全屏 UI 下带宽与上传成本高。
- 字体回退/抗锯齿差异导致跨平台像素级不一致。
- 不同引擎的纹理更新 API 差异大，容易引入线程安全问题。

策略：

- 首先落地“全量上传 + Surface 复用 + 0GC”，保证稳定可用。
- 再按瓶颈引入 Dirty Rect、分辨率缩放、或 GPU backend。
- 将“资源注册表（字体/图片）”作为强约束：缺失即报错，避免静默回退。

# 6 验收条款

- UI 树逻辑不变时，连续帧渲染不产生托管分配（可用 AllocationTests 或基准测试验证）。
- 分辨率变化时，Surface/Texture 生命周期正确（无泄漏、无崩溃）。
- 跨平台在同一字体资源下布局一致（至少关键控件的尺寸/裁剪一致）。
- Present 层对线程约束显式化（Unity/Unreal 不出现跨线程资源访问）。

# 7 外部参考

- Skia 官方：Skia 作为 2D 绘制库的 Surface/Canvas 模型
- SkiaSharp：.NET 侧 Skia 绑定的 Surface/Canvas 生命周期与像素访问口径

