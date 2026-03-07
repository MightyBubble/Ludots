# UI Runtime 执行任务表

Date: 2026-03-07
Status: In Progress
Scope: UI Runtime / Native DOM / Native CSS Profile / Skia Rendering / Text & Fonts / Form / Table / Showcase / Skin Swap / Tween Animation
Type: UI-only execution artifact

## 1 实施摘要

- 已统一 UI Runtime：`UiScene`、`UiNode`、`UiDocument`、`UiElement`
- 已统一解析链：`AngleSharp` 负责 HTML/DOM，`ExCSS` 负责 CSS，`FlexLayoutSharp` 负责主布局
- 已统一三种写法：Compose Fluent、Reactive Fluent、Markup + C# CodeBehind
- 已统一适配层：Web / Raylib 统一消费 `UiScene` / `UiSceneDiff`
- 已移除旧 UI 业务入口：`IUiSystem`、`ShowUiCommand`、`UIRoot.Content`
- UI SSOT 文档不再引入 `v1` / `v2` 后缀；后续能力扩展直接回填到现有文档并记录变更历史
- 当前基线已经可用，但目标范围已扩展到：Skia 高级外观、文本与字体、多语言、表单、表格、结构伪类、Tween 动画、图片九宫格与主题换肤
- 第一批实现已落地：基础渐变、单层阴影、基础描边、`font-family`、`white-space`、文本换行与字体注册基础设施

## 2 已完成基线

| 项目 | 状态 | 证据 |
|------|------|------|
| Runtime 场景树 | Done | `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| Native DOM | Done | `src/Libraries/Ludots.UI/Runtime/UiDocument.cs` |
| CSS 计算样式 | Done | `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| Flex 主布局 | Done | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| Skia 基础渲染 | Done | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` |
| Compose Fluent | Done | `src/Libraries/Ludots.UI/Compose/` |
| Reactive Fluent | Done | `src/Libraries/Ludots.UI/Reactive/` |
| Markup + CodeBehind | Done | `src/Libraries/Ludots.UI.HtmlEngine/Markup/` |
| Skin / Theme 演示 | Done | `mods/UiSkinShowcaseMod/` |
| Runtime Tests | Done | `src/Tests/UiRuntimeTests/` |
| Showcase Tests | Done | `src/Tests/UiShowcaseTests/` |

## 3 已批准待交付范围

| 项目 | 状态 | 当前证据 / 缺口 |
|------|------|------|
| Skia 高级外观：阴影、渐变、描边、滤镜、毛玻璃 | Partial | 已支持渐变/单层阴影/基础描边；`filter` / `backdrop-filter` / 毛玻璃仍未完成：`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| 文本换行、字体族、自定义字体、多语言、RTL/BiDi、复杂字形整形 | Partial | 已支持 `font-family`、`white-space`、基础换行与 `UiFontRegistry`；多语言 shaping / RTL/BiDi 仍未完成：`src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs`、`src/Libraries/Ludots.UI/Runtime/UiFontRegistry.cs` |
| Flex 高级能力：`wrap`、`align-content`、更完整 `gap` 语义 | Planned | 当前已接 `FlexLayoutSharp`，但能力未完整暴露：`src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| 运行时伪类补齐：`focus` 打通 | Partial | 语法与状态枚举已在，焦点流未完成：`src/Libraries/Ludots.UI/Runtime/UiPseudoState.cs`、`src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| 结构伪类：`:first-child`、`:last-child`、`:nth-child()` | Planned | 当前支持矩阵仍为未实现：`docs/reference/ui_native_css_html_support_matrix.md` |
| 表单语义：`radio`、校验状态、选择控件一致行为 | Partial | `input[type=radio]` 仍为 Partial：`docs/reference/ui_native_css_html_support_matrix.md` |
| 表格语义：`table`、`thead`、`tbody`、`tr`、`td`、`th` 原生布局 | Planned | 当前仅按普通容器导入：`docs/reference/ui_native_css_html_support_matrix.md` |
| 图片高级能力：九宫格、`object-fit`、裁剪与主题化图片皮肤 | Planned | `object-fit` 当前未实现：`docs/reference/ui_native_css_html_support_matrix.md` |
| Tween 动画与过渡 | Planned | 当前主线未交付；历史提交 `fb8b167` 存在但不在当前 `main` 祖先链上 |
| Showcase 扩展：外观 / 动画 / 表单 / 表格 / 字体页 | Planned | 当前 Showcase 已覆盖三写法和换肤基线：`mods/UiComposeShowcaseMod/`、`mods/UiReactiveShowcaseMod/`、`mods/UiMarkupShowcaseMod/` |

## 4 已删除旧路径

| 旧路径 | 状态 | 替代 |
|------|------|------|
| `src/Core/UI/IUiSystem.cs` | Removed | `UiScene` |
| `src/Core/Commands/ShowUiCommand.cs` | Removed | `UiScene` 挂载 / 事件 / Diff |
| `UIRoot.Content` | Removed | `UIRoot.MountScene(UiScene)` |
| Web `html/css` 主载荷 | Removed | `UiSceneDiff` |
| 旧 Widget/Reconciler Runtime | Removed | Compose / Reactive / Markup |

## 5 当前验收命令

- `dotnet test src/Tests/UiRuntimeTests/UiRuntimeTests.csproj -v minimal`
- `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj -v minimal`
- `dotnet build mods/HtmlTestMod/HtmlTestMod.csproj -v minimal`
- `dotnet build src/Adapters/Raylib/Ludots.Adapter.Raylib/Ludots.Adapter.Raylib.csproj -v minimal`
- `dotnet build src/Adapters/Web/Ludots.Adapter.Web/Ludots.Adapter.Web.csproj -v minimal`
- `powershell -ExecutionPolicy Bypass -File scripts/validate-docs.ps1`

## 6 UI 文档单一真相

- 支持矩阵：`docs/reference/ui_native_css_html_support_matrix.md`
- ADR：`docs/adr/ADR-0002-ui-runtime-unification.md`
- RFC：`docs/rfcs/RFC-0001-ui-runtime-fluent-authoring.md`

## 7 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立执行表 | 回填统一 Runtime、DOM、CSS、Flex、三写法与 Showcase 基线 |
| 2026-03-07 | 扩大目标范围 | 新目标纳入 Skia 高级外观、文本/字体、多语言、表单、表格、结构伪类、Tween 动画、图片九宫格 |
| 2026-03-07 | 取消文档版本尾缀 | 后续扩展不再新建 `v1` / `v2` 文档，直接更新现有 SSOT |
