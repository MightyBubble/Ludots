# 统一 UI Runtime 与三前端写法

本文记录 Ludots 当前 UI 体系的正式实现：一个原生 C# UI Runtime，同时承载 Compose、Reactive、Markup 三种写法；HTML/CSS 只是其中一种 authoring 入口，不再形成第二套运行时。

## 1 目标与边界

当前 UI 体系的硬约束如下：

- 运行时唯一真相位于 `src/Libraries/Ludots.UI/`，不再允许并行 UI Runtime。
- 三种写法——Compose、Reactive、Markup——必须落到同一套 `UiScene` / `UiElement` / `UiStyle` / `UiLayoutEngine` / `UiSceneRenderer` 管线。
- HTML/CSS 负责 authoring 与设计稿映射；脚本层保持纯 C#，不引入 JS Runtime。
- 所有外部依赖优先使用仓库内源码入口，不使用隐藏的二进制 fallback。

## 2 统一运行时结构

UI 主干运行时位于 `src/Libraries/Ludots.UI/`：

- 场景与节点：`src/Libraries/Ludots.UI/Runtime/UiScene.cs`、`src/Libraries/Ludots.UI/Runtime/UiElement.cs`
- 样式与选择器：`src/Libraries/Ludots.UI/Runtime/UiStyle.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`、`src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs`
- 布局：`src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`
- 渲染：`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`
- 交互与输入：`src/Libraries/Ludots.UI/Input/InputEvent.cs`、`src/Libraries/Ludots.UI/Input/PointerEvent.cs`、`src/Libraries/Ludots.UI/Input/NavigationEvent.cs`
- Reactive 写法入口：`src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
- Compose 写法入口：`src/Libraries/Ludots.UI/Compose/Ui.cs`、`src/Libraries/Ludots.UI/Compose/UiSceneComposer.cs`

统一执行链路如下：

1. Compose / Reactive / Markup 任一入口产出 `UiScene`
2. `UiLayoutEngine` 调用 `FlexLayoutSharp` 进行盒模型与 Flex 布局
3. `UiSceneRenderer` 使用 Skia 渲染文本、几何、阴影、模糊、图片与 SVG
4. 输入事件回流到同一套节点树，驱动 hover / focus / active / checked / scroll / selection / animation

## 3 三种写法的归一关系

### 3.1 Compose

Compose 是直接以 C# 构造 UI 树的写法，入口由 showcase 工厂示例覆盖：

- `mods/UiShowcaseCoreMod/Showcase/UiShowcaseFactory.cs`
- `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`

Compose 直接构造 `UiElement` 树，但布局、样式、渲染、交互全部复用统一 Runtime。

### 3.2 Reactive

Reactive 是基于状态重建 UI 的写法，入口由下列路径覆盖：

- `src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
- `mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`
- `mods/UiShowcaseCoreMod/Showcase/ReactiveShowcaseState.cs`

Reactive 不引入独立 reconciler 运行时；状态变化最终仍然落到同一套 `UiScene`。

### 3.3 Markup

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

## 4 外部依赖单源真相

| 组件 | 源码唯一入口 | 编译归属 | 直接消费者 |
|------|--------------|----------|------------|
| FlexLayoutSharp | `src/Libraries/Ludots.UI/FlexLayoutSharp/` | `src/Libraries/Ludots.UI/Ludots.UI.csproj` | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| AngleSharp | `src/Libraries/Ludots.UI.HtmlEngine/External/AngleSharp/src/` | `src/Libraries/Ludots.UI.HtmlEngine/Ludots.UI.HtmlEngine.csproj` | `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs` |
| ExCSS | `src/Libraries/Ludots.UI.HtmlEngine/External/ExCSS/src/` | `src/Libraries/ExCSS/ExCSS.csproj` | `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs`、`src/Libraries/Svg.Skia/Svg.Custom/Svg.Custom.csproj` |
| SkiaSharp | `src/Libraries/SkiaSharp/` | `src/Libraries/SkiaSharp/SkiaSharp/SkiaSharp.csproj` | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` |
| Svg.Skia | `src/Libraries/Svg.Skia/` | `src/Libraries/Svg.Skia/Svg.Skia/Svg.Skia.csproj` | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`、`mods/UiShowcaseCoreMod/Showcase/UiShowcaseImageAssets.cs` |

本次收束后的关键规则：

- `src/Libraries/Ludots.UI.HtmlEngine/Ludots.UI.HtmlEngine.csproj` 只显式编译 `Markup/`、`Properties/` 与 `External/AngleSharp/src/`
- `src/Libraries/ExCSS/ExCSS.csproj` 只显式编译 `src/Libraries/Ludots.UI.HtmlEngine/External/ExCSS/src/`
- `Ludots.UI.HtmlEngine` 内部旧的 AngleSharp / ExCSS 并行副本目录已从运行时真相中移除，不再参与编译

## 5 原生渲染与运行时加载

Skia 原生库必须与可执行文件位于同级输出根目录，而不是仅放在 `runtimes/<rid>/native/` 子目录。当前通过以下工程保证：

- `src/Client/Ludots.Client.Raylib/Ludots.Client.Raylib.csproj`
- `src/Tools/Ludots.UI.ShowcaseCapture/Ludots.UI.ShowcaseCapture.csproj`
- `src/Tests/UiShowcaseTests/UiShowcaseTests.csproj`

这三处工程同时：

- 保留 `runtimes/**` 目录拷贝
- 额外把当前平台 `libSkiaSharp` 复制到输出根目录

这样 `SkiaSharp` 的 `LibraryLoader` 才能稳定命中原生库，避免 `DllNotFoundException`。

## 6 官方验收入口

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

## 7 相关文档

- 架构决策：`docs/adr/ADR-0002-unified-ui-runtime-and-authoring-models.md`
- 表现层与渲染分层：`docs/architecture/presentation_performer.md`
- Mod 运行时单一真相：`docs/architecture/mod_runtime_single_source_of_truth.md`
