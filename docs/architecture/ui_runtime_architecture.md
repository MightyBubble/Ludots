# 统一 UI Runtime 与三前端写法

本文记录 Ludots 当前 UI 体系的正式实现：一个原生 C# UI Runtime，同时承载 Compose、Reactive、Markup 三种写法；HTML/CSS 只是其中一种 authoring 入口，不再形成第二套运行时。

## 1 目标与边界

当前 UI 体系的硬约束如下：

- UI 模型层唯一真相位于 `src/Libraries/Ludots.UI/`，零平台依赖（不引用 SkiaSharp 或任何渲染后端）。
- Skia 渲染适配层位于 `src/Libraries/Ludots.UI.Skia/`，作为独立程序集，由平台 Adapter 调用。
- 三种写法——Compose、Reactive、Markup——必须落到同一套 `UiScene` / `UiElement` / `UiStyle` / `UiLayoutEngine` 管线。
- 布局和渲染通过策略接口（`IUiTextMeasurer`、`IUiImageSizeProvider`、`IUiRenderer`）注入，不硬编码后端。
- HTML/CSS 负责 authoring 与设计稿映射；脚本层保持纯 C#，不引入 JS Runtime。
- 所有外部依赖优先使用仓库内源码入口，不使用隐藏的二进制 fallback。

## 2 分层结构

### 2.1 Ludots.UI（平台无关模型层）

位于 `src/Libraries/Ludots.UI/`，零 SkiaSharp 依赖：

- 场景与节点：`Runtime/UiScene.cs`、`Runtime/UiElement.cs`、`Runtime/UiNode.cs`
- 颜色：`Runtime/UiColor.cs`（平台无关 RGBA struct，替代 SKColor）
- 样式与选择器：`Runtime/UiStyle.cs`、`Runtime/UiStyleResolver.cs`、`Runtime/UiSelectorParser.cs`
- 布局：`Runtime/UiLayoutEngine.cs`（通过 `IUiTextMeasurer` / `IUiImageSizeProvider` 注入）
- 变换数学：`Runtime/UiTransformMath.cs`（使用 `System.Numerics.Matrix3x2`，无 SKMatrix）
- 交互与输入：`Input/InputEvent.cs`、`Input/PointerEvent.cs`、`Input/NavigationEvent.cs`
- Reactive 写法入口：`Reactive/ReactivePage.cs`
- Compose 写法入口：`Compose/Ui.cs`、`Compose/UiSceneComposer.cs`

策略接口（Port）：

| 接口 | 职责 | 注入点 |
|------|------|--------|
| `IUiTextMeasurer` | 文字测量（行断、省略、宽高） | `UiLayoutEngine` 构造函数 |
| `IUiImageSizeProvider` | 图片固有尺寸查询 | `UiLayoutEngine` 构造函数 |
| `IUiRenderer` | 渲染完整场景 | `UIRoot` 构造函数 |
| `IUiCanvasContent` | Canvas 自定义绘制回调的 marker interface | `UiNode.CanvasContent` |

### 2.2 Ludots.UI.Skia（Skia 渲染适配层）

位于 `src/Libraries/Ludots.UI.Skia/`，引用 `Ludots.UI` + `SkiaSharp` + `Svg.Skia`：

- 渲染器：`SkiaUiRenderer`（实现 `IUiRenderer`，内部持有 `SKCanvas`）
- 文字测量：`SkiaTextMeasurer`（实现 `IUiTextMeasurer`，基于 `SKFont` / `SKPaint`）
- 图片尺寸：`SkiaImageSizeProvider`（实现 `IUiImageSizeProvider`，基于 `UiImageSourceCache`）
- 字体注册：`UiFontRegistry`（SKTypeface 解析与缓存）
- 图片缓存：`UiImageSourceCache`（SKImage / SVG 加载与缓存）
- 文字排版：`UiTextLayout`（行断、换行、省略号、文字 Run 拆分）
- Canvas 内容：`UiCanvasContent`（实现 `IUiCanvasContent`，持有 `Action<SKCanvas, SKRect>` 回调）
- 边界转换：`UiSkiaExtensions`（`UiColor.ToSKColor()`、`SKColor.ToUiColor()`、`Matrix3x2.ToSKMatrix()`）

### 2.3 依赖方向

```
Ludots.UI              → 零平台依赖（仅 FlexLayoutSharp）
Ludots.UI.Skia         → Ludots.UI + SkiaSharp + Svg.Skia
Ludots.UI.HtmlEngine   → Ludots.UI + Ludots.Core
Adapter (Raylib/Web)   → Ludots.UI + Ludots.UI.Skia + Core
```

Ludots.UI **绝不** 引用 Ludots.UI.Skia 或任何渲染后端。

## 3 统一执行链路

1. Compose / Reactive / Markup 任一入口产出 `UiScene`（需注入 `IUiTextMeasurer` + `IUiImageSizeProvider`）
2. `UiLayoutEngine` 通过策略接口测量文字和图片，调用 `FlexLayoutSharp` 进行盒模型与 Flex 布局
3. `IUiRenderer` 实现（当前为 `SkiaUiRenderer`）渲染文本、几何、阴影、模糊、图片与 SVG
4. 输入事件回流到同一套节点树，驱动 hover / focus / active / checked / scroll / selection / animation

## 4 三种写法的归一关系

### 4.1 Compose

Compose 是直接以 C# 构造 UI 树的写法，入口由 showcase 工厂示例覆盖：

- `mods/UiShowcaseCoreMod/Showcase/UiShowcaseFactory.cs`
- `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`

Compose 直接构造 `UiElement` 树，但布局、样式、渲染、交互全部复用统一 Runtime。

### 4.2 Reactive

Reactive 是基于状态重建 UI 的写法，入口由下列路径覆盖：

- `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
- `mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`
- `mods/UiShowcaseCoreMod/Showcase/ReactiveShowcaseState.cs`

Reactive 不引入独立 reconciler 运行时；状态变化最终仍然落到同一套 `UiScene`。

### 4.3 Markup

Markup 是 HTML/CSS 到原生 UI DOM 的 authoring 入口，入口位于：

- `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`
- `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs`
- `mods/UiShowcaseCoreMod/UiShowcaseCoreMod.Assets.Showcase.markup_showcase.html`
- `mods/UiShowcaseCoreMod/UiShowcaseCoreMod.Assets.Showcase.markup_showcase.css`
- `mods/UiShowcaseCoreMod/Showcase/MarkupShowcaseCodeBehind.cs`

Markup 负责：

- 用 `AngleSharp` 解析 HTML DOM
- 用 `ExCSS` 解析 CSS 规则
- 将 DOM 与样式映射为 `UiDocument` / `UiElement`
- 通过 C# code-behind 绑定交互

Markup 不负责：

- JS 执行
- 浏览器兼容层
- 第二套渲染树

### 4.4 Browser UI

Browser UI 是独立的 browser surface runtime，用于运行真正的 Web App。它不改变 Markup 的边界，也不把 JS Runtime 引入 Compose / Reactive / Markup。

入口位于：

- `src/Libraries/Ludots.UI.Browser/`
- `src/Libraries/Ludots.UI.Browser.Skia/`

Browser UI 负责：

- 定义平台无关 `IBrowserRuntime` / `IBrowserSurface`
- 接收浏览器引擎输出的 `BrowserFrame` 像素帧与 dirty rect
- 通过 `BrowserCanvasContent` 复用 `Ui.Canvas(...)` 嵌入现有 `UiScene`
- 通过 `IBrowserMessageBridge` 连接 Web App 与 C# host API
- 为现有 `Ludots.WebUI` 这类上层 Mod 事件桥提供底层 transport

Browser UI 不负责：

- CEF / Ultralight 的具体进程、线程与原生生命周期
- UE5 / Unity / Godot 等商业引擎 adapter wiring
- 把 native Markup 升级成浏览器兼容层
- 替代 `Ludots.WebUI` 的 Mod-facing event facade

正式内置 provider 只保留：

- CEF：完整 Chromium 兼容基线，面向“任意 Web App”
- Ultralight：轻量游戏 UI provider，面向 Ludots 自有 Web bundle

## 5 外部依赖单源真相

| 组件 | 源码唯一入口 | 编译归属 | 直接消费者 |
|------|--------------|----------|------------|
| FlexLayoutSharp | `src/Libraries/Ludots.UI/FlexLayoutSharp/` | `Ludots.UI.csproj` | `UiLayoutEngine.cs` |
| AngleSharp | `src/Libraries/Ludots.UI.HtmlEngine/External/AngleSharp/src/` | `Ludots.UI.HtmlEngine.csproj` | `UiMarkupLoader.cs` |
| ExCSS | `src/Libraries/Ludots.UI.HtmlEngine/External/ExCSS/src/` | `ExCSS.csproj` | `UiCssParser.cs`、`Svg.Custom.csproj` |
| SkiaSharp | `src/Libraries/SkiaSharp/` | `SkiaSharp.csproj` | `Ludots.UI.Skia/SkiaUiRenderer.cs` |
| Svg.Skia | `src/Libraries/Svg.Skia/` | `Svg.Skia.csproj` | `Ludots.UI.Skia/SkiaUiRenderer.cs`、`UiShowcaseImageAssets.cs` |
| Browser UI contracts | `src/Libraries/Ludots.UI.Browser/` | `Ludots.UI.Browser.csproj` | CEF/Ultralight provider adapters, `Ludots.UI.Browser.Skia` |
| Browser UI Skia adapter | `src/Libraries/Ludots.UI.Browser.Skia/` | `Ludots.UI.Browser.Skia.csproj` | platform adapters that host browser surfaces through Skia UI |

关键规则：

- `Ludots.UI.csproj` 不引用 SkiaSharp 或 Svg.Skia
- `Ludots.UI.Skia.csproj` 是 SkiaSharp 和 Svg.Skia 的唯一 UI 侧消费者
- `Ludots.UI.HtmlEngine.csproj` 只显式编译 `Markup/`、`Properties/` 与 `External/AngleSharp/src/`
- `Ludots.UI.Browser.csproj` 不引用 SkiaSharp、CEF native、Ultralight native 或任何平台 Adapter
- 浏览器内核实现必须作为 `IBrowserRuntime` / `IBrowserSurface` 的 adapter 接入
- 正式内置 provider 只允许 CEF 与 Ultralight；Core 主线不接收第三种 browser provider
- `Ludots.WebUI` 是上层事件/API 门面；如果需要接真实 browser surface，应在实现层复用 `IBrowserMessageBridge`，不要另建平行 browser runtime

## 6 原生渲染与运行时加载

Skia 原生库必须与可执行文件位于同级输出根目录，而不是仅放在 `runtimes/<rid>/native/` 子目录。当前通过以下工程保证：

- `src/Client/Ludots.Client.Raylib/Ludots.Client.Raylib.csproj`
- `src/Tools/Ludots.UI.ShowcaseCapture/Ludots.UI.ShowcaseCapture.csproj`
- `src/Tests/UiShowcaseTests/UiShowcaseTests.csproj`

这三处工程同时：

- 保留 `runtimes/**` 目录拷贝
- 额外把当前平台 `libSkiaSharp` 复制到输出根目录

这样 `SkiaSharp` 的 `LibraryLoader` 才能稳定命中原生库，避免 `DllNotFoundException`。

## 7 官方验收入口

统一 UI 体系的正式验收入口如下：

- 自动化验收：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`
- 截图工具：`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs`
- Showcase 工厂：`mods/UiShowcaseCoreMod/Showcase/UiShowcaseFactory.cs`

本地可见证据位于：

- `artifacts/acceptance/ui-showcase-compose/`
- `artifacts/acceptance/ui-showcase-reactive/`
- `artifacts/acceptance/ui-showcase-markup/`
- `artifacts/acceptance/ui-showcase-style-parity/`
- `artifacts/acceptance/ui-showcase-skin-swap/`

这些产物用于证明三种写法、皮肤切换、表单、表格、滚动、裁剪、动画、图片、文本与样式 parity 已接入同一套 Runtime。

## 8 相关文档

- 架构决策：`docs/adr/ADR-0002-unified-ui-runtime-and-authoring-models.md`
- 浏览器 UI 决策：`docs/adr/ADR-0003-browser-ui-runtime-contract.md`
- 浏览器 UI Runtime：`docs/architecture/browser_ui_runtime.md`
- 适配器模式与平台抽象：`docs/architecture/adapter_pattern.md`
- 表现层与渲染分层：`docs/architecture/presentation_presenter.md`
- Mod 运行时单一真相：`docs/architecture/mod_runtime_single_source_of_truth.md`
