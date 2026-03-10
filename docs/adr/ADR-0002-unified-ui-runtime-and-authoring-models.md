# ADR-0002 统一 UI Runtime 与三前端写法

## 状态

Accepted

## 背景

Ludots 需要一个原生 C# UI 框架，同时满足以下目标：

- 支持 Compose、Reactive、HTML/CSS Markup 三种开发入口
- 仍然保持 Mod 架构与多平台适配
- 不引入 JS Runtime
- 不保留旧 UI 实现与兼容分支
- 外部依赖全部以仓库源码为主，不依赖隐藏的二进制回退

历史上 UI 相关能力曾出现过重复入口：

- HTML/CSS 侧存在与正式 vendored 源并行的 `AngleSharp` / `ExCSS` 副本
- Markup authoring 与原生 Runtime 的边界不够明确，容易滑向第二套运行时

这会带来重复类型、编译污染、依赖来源不清和运行时加载不稳定。

## 决策

采用以下统一方案：

1. `src/Libraries/Ludots.UI/` 作为唯一 UI Runtime
2. Compose、Reactive、Markup 都必须落到同一套 `UiScene` / `UiElement` / `UiLayoutEngine` / `UiSceneRenderer`
3. `Markup` 仅作为 authoring 入口，由 `AngleSharp + ExCSS + C# code-behind` 转换到原生 UI DOM
4. `AngleSharp` 源码唯一入口固定为 `src/Libraries/Ludots.UI.HtmlEngine/External/AngleSharp/src/`
5. `ExCSS` 源码唯一入口固定为 `src/Libraries/Ludots.UI.HtmlEngine/External/ExCSS/src/`
6. 删除 `Ludots.UI.HtmlEngine` 内部旧的 AngleSharp / ExCSS 并行副本编译真相
7. Skia 原生库按当前平台复制到应用输出根目录，消除 `libSkiaSharp` 运行时探测失败

## 影响

正向影响：

- 三种写法共享一套布局、渲染、交互、动画与皮肤系统
- HTML/CSS 能作为设计稿导入入口，但不会再分叉出浏览器式运行时
- 第三方依赖来源清晰，可在仓库内完整审计与修改
- Showcase、截图工具与验收测试能对同一套 Runtime 产生证据

约束与代价：

- 不支持 JS 脚本执行
- Markup 能力边界由原生 Runtime 决定，不追求浏览器全兼容
- vendored 第三方源码会保留其现有 warning，需要后续单独治理，但不再允许重复真相

## 证据

- 运行时主干：`src/Libraries/Ludots.UI/`
- Markup 入口：`src/Libraries/Ludots.UI.HtmlEngine/Markup/`
- 单源工程：`src/Libraries/Ludots.UI.HtmlEngine/Ludots.UI.HtmlEngine.csproj`、`src/Libraries/ExCSS/ExCSS.csproj`
- 验收测试：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`
- 可见验收：`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs`
