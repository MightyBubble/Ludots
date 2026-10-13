# Skia GPU 覆盖层适配指南（Unity / Unreal 宿主）

本页写给要在自家引擎宿主里驱动 Ludots Skia UI 的适配作者：Unity、UE5、Godot 或任何自建桌面宿主。读完应能不猜地把 Skia 接到宿主 GPU 上下文上，并知道哪些坑是合同明令禁止的。

Raylib 宿主是本合同的参考实现。商业引擎宿主适配的仓库归属见 `docs/architecture/adapter_pattern.md` §7：Core 只提供平台无关 contract 与本文合同，UE5 / Unity / Godot 的 world、level、component、脚本绑定归下游仓库。

## Decision

Skia 在宿主帧路径上必须 GPU 驱动，默认开启。CPU 光栅只作为环境变量显式配置的回退（云 Linux 无 GPU 环境的验收依赖它）；production 路径 GPU 渲染失败直接抛异常，禁止静默降级。

## Reuse List

适配直接复用这些引擎无关层，它们只依赖 `SKCanvas`，不包含任何宿主特定代码：

- `Ludots.Presentation.Skia.SkiaOverlayRenderer`：把 `PresentationOverlayScene`（HUD 批，UnderUi / TopMost 分层）画到任意 `SKCanvas`，含 lane 分帧刷新计划（`PresentationOverlayLanePacer`）。
- `Ludots.UI.Skia`：retained UI 的渲染器与控件（`SkiaUiRenderer`、`ISkiaUiCanvasContent`、`Label` / `Panel` 等），画到 `SKCanvas` 或直接渲染进 `SKSurface`。
- `Ludots.UI`：`UIRoot` / `UiSurfaceHost` / `UiScene` 场景合成与 `UIRoot.IsDirty` 脏门。
- `Ludots.Core.Presentation.Hud`：`PresentationOverlayScene` 层版本脏跟踪与 SoA 批缓冲。

仓库内嵌的 SkiaSharp（`src/Libraries/SkiaSharp/`）在 win-x64 `libSkiaSharp.dll` 里带全 `gr_direct_context_make_gl / _vulkan / _direct3d / _metal` 四个后端入口；其他平台先验证对应 runtimes 目录的导出符号再选后端。

适配不得做的事：给这些引擎无关层加宿主依赖；新建第二套 UI 合成器或第二个 `UIRoot`；为"GPU 不可用"自动加 CPU 回退。

## 四个接缝

### 接缝 1：图形上下文绑定

在宿主当前 GPU 上下文上创建 Skia 上下文：

```csharp
GRGlInterface glInterface = GRGlInterface.Create()
    ?? throw new InvalidOperationException(...);
GRContext context = GRContext.CreateGl(glInterface)
    ?? throw new InvalidOperationException(...);
```

参考实现：`src/Client/Ludots.Raylib.Render/Rendering/RaylibSkiaGlContext.cs`。函数解析必须走 SkiaSharp 原生 GL 接口，禁止 pinvoke `opengl32.dll` 或任何平台 WGL 路径（`NativeSkiaOverlayTests.RaylibSkiaOverlay_UsesNativeGlInterfaceInsteadOfWindowsWgl` 钉住这条）。

非 GL 宿主（Unity D3D11、UE RHI）用对应 `GRContext.CreateDirect3D / CreateVulkan / CreateMetal`，上下文与纹理都来自宿主设备，见下文分引擎路线。

### 接缝 2：GPU 表面

把宿主 render target 包成 Skia 可画的目标。render-texture 形态：

```csharp
var target = new GRBackendRenderTarget(width, height,
    sampleCount: 0, stencilBits: 8,
    new GRGlFramebufferInfo(hostTextureId, 0x8058 /* GL_RGBA8 */));
SKSurface surface = SKSurface.Create(context, target,
    GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
```

GL render texture 的行序是 bottom-left，Skia 必须按 `BottomLeft` 解释；上屏时宿主的负高度采样翻转才能把内容摆正。两处取向缺一不可，只做一边会把整层上下镜像。

默认帧缓冲直写形态：`GRGlFramebufferInfo(0, 0x8058)` + `GRSurfaceOrigin.BottomLeft`，不经过中间纹理。参考实现：`RaylibSkiaGpuOverlaySurface`（render-texture）与 `RaylibSkiaFramebufferOverlaySurface`（直写）。

宿主与 Skia 共享同一份图形状态，因此有两条硬不变量：

1. **每批 GPU 渲染前 `context.ResetContext(GRGlBackendState.All)`**。宿主在两批 Skia 渲染之间动过状态，Skia 的状态缓存必须重置，否则输出错乱。
2. **`surface.Flush(submit: true)` + `context.Submit()` 之后，宿主才能消费这张纹理**。未提交就拿来采样 / blit，是未定义行为。

render-texture 上屏时用 premultiplied alpha 混合，并且 GL render texture 的原点在左下，宿主采样要翻 Y。

### 接缝 3：分层与合成顺序

一帧的绘制顺序固定：

```text
世界 / 后处理
  → 浏览器层（若宿主有）
  → UnderUi HUD（GPU 直绘或 render-texture 上屏）
  → UI 面板层（render-texture 上屏）
  → TopMost HUD
  → 宿主自绘诊断（若开）
```

参考实现：`RaylibOverlayCompositor.Render` 的终绘段。面板挂载不得把 UnderUi 世界 HUD 踢回整窗光栅（`gitbook/architecture/ui-rendering-and-surface-ownership.md` §6 同一条铁律）。

### 接缝 4：策略姿态

- **默认 GPU 开**，回退只能由环境变量显式配置。命名规则：`LUDOTS_<HOST>_DISABLE_SKIA_GPU_*`。
- **retained UI（面板层）用 render-texture + 脏门控**：只在 `!hadContent || UIRoot.IsDirty` 时重渲，否则直接复用缓存纹理；脏标记归 `UiSurfaceHost`，适配不得自写 `IsDirty`。
- **每帧全变的 HUD** 才值得默认帧缓冲直写（省一张中间纹理，代价是每帧全量重绘）。
- **production 路径失败即抛**。render-texture 表面在无 GPU 环境（软件 GL、远程会话）下创建失败时，参考实现先记一次性 Warn 说明原因，随后启用 GPU 的 production 路径渲染失败抛 `InvalidOperationException`——Warn 是定位诊断，不是降级许可。要跑光栅就用 kill-switch 显式配置。
- 适配完成时打一行可断言的日志：`GPU Accelerated: True (<宿主> <路径>)`，回退路径打 `GPU Accelerated: False (<路径>)`。

Raylib 参考实现的 kill-switch（其他宿主照此命名登记）：

| 环境变量 | 关掉什么 | 默认 |
|---|---|---|
| `LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY` | UnderUi HUD 的 GPU 直绘，回退整窗光栅合成 | 未设置 = GPU 开 |
| `LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY` | 默认帧缓冲直写，保留 render-texture GPU | 未设置 = 直写开 |
| `LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UI` | UI 面板层 GPU render-texture，回退光栅 + 纹理上传 | 未设置 = GPU 开 |

取值 `1 / true / yes / on`（`ReadEnvBool` 语义）。

## Unity 路线

两条路，先选图形 API 再写代码：

1. **Player Settings 强制 OpenGL 图形 API**：与 Raylib 参考实现完全同构，接缝 1 / 2 代码近乎照抄。风险在 Unity GL 后端自身的质量与平台覆盖（macOS / 部分移动端不适用）。
2. **保持默认 D3D11**：native 插件拿宿主设备（`UnityGraphicsD3D11Device` 回调）与 `Texture2D.GetNativeTexturePtr()`，`GRContext.CreateDirect3D` + `GRD3DBackendContext` 建 Skia 上下文，渲染进 Unity RenderTexture 的原生指针。代价是必须写一个 C++ interop 插件。

约束：

- Skia 渲染必须发生在持有该上下文的渲染线程 / CommandBuffer 回调里，不能在主线程 `Update` 里直接画。
- 迁移次序：第一步 CPU 光栅 + `LoadRawTextureData` 上传（等价于 Raylib 已淘汰的光栅合成路径，先把合同跑通）；第二步再把面板层换成 GPU 表面。HUD 批渲染两条路都可以直接 GPU。
- backdrops blur 等读回操作只允许在 GPU 表面上 Snapshot（纹理像，无 CPU 回读），禁止为模糊单独建整窗 CPU 中间面。

## Unreal 路线

UE 是 C++ 宿主，不建议嵌 .NET 跑 SkiaSharp 再跨语言共享纹理——interop 成本高于合同本身。适配形态：

- 消费 Core 的平台无关输出：`PresentationOverlayScene` 快照（HUD 批）与 UI 画布内容 contract；引擎侧用原生 Skia 绑 UE RHI（Vulkan / D3D12 / Metal 的 `GrDirectContext`）。
- 四个接缝的合同同样成立：`resetContext` 每批调用、flush + submit 后再让 RHI 采样、分层顺序不变、kill-switch 用 `LUDOTS_UNITY_*` 同款命名规则登记。
- 若 UE 侧复用 Ludots 的浏览器 UI（CEF bootstrap），先读 `gitbook/architecture/browser-runtime-provider-adapter-guide.md`，那是另一份独立合同。

## 禁做清单

- ❌ GPU 创建 / 渲染失败时静默回退 CPU 光栅。回退只能显式配置。
- ❌ 跳过 `ResetContext` 直接渲染；或未 `Flush + Submit` 就让宿主消费纹理。
- ❌ 用 CPU 读回（`ReadPixels` / 整窗中间光栅面）实现 blur 或截图以外的帧路径需求。
- ❌ 给 `Ludots.Presentation.Skia` / `Ludots.UI.Skia` / `Ludots.UI` 加宿主依赖。
- ❌ 新建第二套合成器、第二个 `UIRoot`，或自写 `IsDirty` 强刷。
- ❌ 把上万条 HUD 血条画进 retained UI 场景（走 `PresentationOverlayScene` 批渲染，见 `ui-rendering-and-surface-ownership.md` §5）。

## 验收清单

适配合入前逐条核对：

- 默认配置下日志出现 `GPU Accelerated: True (<宿主> <路径>)`，三层（UnderUi / UI / TopMost）都有归属。
- kill-switch 全开后全链光栅仍可跑完一次真实启动验收（云 Linux 验收路径）。
- 面板挂载时 UnderUi HUD 仍在 GPU 路径（不因 `hasUiLayer` 回退）。
- `UIRoot.IsDirty` 为 false 的帧不重渲面板层、不产生纹理上传。
- 与宿主自绘内容交错渲染时无 GL 状态污染（`ResetContext` 生效的回归用例）。
- 图形 API / 后端符号验证留痕（win-x64 四后端符号已在本仓库验证）。
- 源合同测试：用 `NativeSkiaOverlayTests` 的源文本断言模式钉住本页不变量（`ResetContext` 存在、`UpdateTexture` 只在回退分支、kill-switch 命名），新宿主适配照此建自己的合同测试。

## 参考实现导航

- GL 上下文工厂：`src/Client/Ludots.Raylib.Render/Rendering/RaylibSkiaGlContext.cs`
- GPU render-texture 画布表面：`src/Client/Ludots.Raylib.Render/Rendering/RaylibSkiaGpuCanvasSurface.cs`
- 默认帧缓冲直写表面：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibSkiaFramebufferOverlaySurface.cs`
- 分层合成器：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibOverlayCompositor.cs`
- UI 场景渲染（直渲目标面 + backdrop blur Snapshot）：`src/Libraries/Ludots.UI.Skia/UiSceneRenderer.cs`
- 渲染分层口径：`gitbook/architecture/ui-rendering-and-surface-ownership.md`
- 决策记录：`docs/adr/ADR-0005-skia-gpu-surface-contract.md`
