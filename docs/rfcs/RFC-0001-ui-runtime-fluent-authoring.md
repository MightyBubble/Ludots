# RFC-0001 统一 UI Runtime、Native DOM 与 Fluent Authoring API 草案

本提案给出 Ludots 统一 UI 体系的正式实施结果：一个统一 UI Runtime，以 Native DOM + Native CSS Profile 作为结构与样式中间层，提供三种官方写法，零 DSL，动态行为全部使用 C#。

## 0 实现回填（2026-03-07）

截至 2026-03-07，本 RFC 的主体方案已经在仓库中落地并成为正式规范：

- 统一 Runtime：`src/Libraries/Ludots.UI/Runtime/UiScene.cs`、`src/Libraries/Ludots.UI/Runtime/UiNode.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`
- Native DOM：`src/Libraries/Ludots.UI/Runtime/UiDocument.cs`、`src/Libraries/Ludots.UI/Runtime/UiElement.cs`、`src/Libraries/Ludots.UI/Runtime/UiElementSelectorMatcher.cs`
- 主布局后端：`src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`，由 `FlexLayoutSharp` 驱动
- 三种官方写法：`src/Libraries/Ludots.UI/Compose/`、`src/Libraries/Ludots.UI/Reactive/`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/`
- Web SceneDiff 主载荷：`src/Adapters/Web/Ludots.Adapter.Web/Services/WebUiSystem.cs`
- 自动化与可见验收：`src/Tests/UiRuntimeTests/`、`src/Tests/UiShowcaseTests/`

旧路径的收敛状态：

- `IUiSystem.SetHtml(string html, string css)`：Removed
- `ShowUiCommand`：Removed
- `UIRoot.Content`：Removed
- Web `html/css` 字符串主载荷：Removed
- 旧 Widget/Reconciler Runtime：Removed

## 1 目标

本 RFC 解决了以下问题：

- 统一历史上的 HTML 字符串 UI、Reactive Widget 树 UI 和 Web 字符串运输分裂契约。
- 提供一套原生 C#、可链式调用、可多平台适配的 UI 编写体验。
- 同时覆盖 Flutter-like、React-like、传统 HTML/CSS + C# CodeBehind 三类作者心智模型。
- 提供 Native DOM，使设计原型可直接解析进入统一 Runtime。
- 提供 Native CSS Profile，使 CSS 样式可以快速匹配设计稿与主题系统。

## 2 非目标

以下能力仍然不在目标范围内：

- 浏览器级完整 HTML/CSS/DOM/JS 兼容
- 任意 JS 执行、模板表达式 DSL、脚本小语言
- 浏览器级 CSS Grid、CSSOM、浏览器怪癖兼容

以下能力已从“后续想法”提升为**批准纳入统一 UI 路线**，但当前仓库尚未全部交付：

- 基于 Skia 的高级外观：阴影、渐变、描边、滤镜、毛玻璃
- 文本与字体：换行、自定义字体、字体族、字体回退、多语言、RTL/BiDi、复杂字形整形
- 表单与表格语义：`radio`、校验状态、`table` / `thead` / `tbody` / `tr` / `td` / `th`
- 结构伪类与运行时伪类补齐
- 动画/过渡：统一走 Tween 基建，不引入第二套脚本或动画运行时
- 图片能力：九宫格、裁剪、`object-fit`、皮肤化资源切换

## 3 三种官方写法

### 3.1 Compose Fluent

定位：主游戏 UI 的官方默认写法。

适用场景：HUD、战斗面板、背包、技能栏、移动端 UI、性能敏感场景。

### 3.2 Reactive Fluent

定位：状态驱动型、组件型、工具型 UI。

适用场景：调试面板、配置页、统计界面、数据驱动菜单、复杂列表。

### 3.3 Markup + C# CodeBehind

定位：HTML/CSS 风格作者体验，但行为层全部使用 C#；设计原型先进入 Native DOM，再由 CodeBehind 补行为。

适用场景：设计原型导入、剧情页、公告/帮助页、主题化页面、富文本内容。

## 4 规范写法

### 4.1 允许的写法

- 使用 Fluent API 构建节点树
- 使用强类型 C# 方法作为事件绑定目标
- 在 Markup 中使用 HTML/CSS 描述结构与样式，使用 C# CodeBehind 绑定动态行为
- 使用统一 Theme Token、Style Token 和状态选择器

### 4.2 禁止的写法

- 恢复 `UIRoot.Content` 作为业务入口
- 恢复 `IUiSystem.SetHtml(string html, string css)` 作为生产契约
- 引入模板 DSL、表达式语言或 JS 执行层
- 让 Web 和 Raylib 各自维护独立 UI 语义

## 5 相关文档

- ADR：`docs/adr/ADR-0002-ui-runtime-unification.md`
- 支持矩阵：`docs/reference/ui_native_css_html_support_matrix.md`
- 执行任务表：`artifacts/ui-runtime-execution-task-table.md`

## 6 文档治理与变更记录

- UI 规范文档采用固定路径维护，不新增 `v1` / `v2` 后缀文档。
- 范围调整、能力批准、验收口径变化，直接回填到现有 RFC / ADR / 支持矩阵 / 执行任务表。
- 本 RFC 中已批准但未交付的能力，以 `artifacts/ui-runtime-execution-task-table.md` 为执行真相。

## 7 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | RFC 回填落地状态 | 统一 Runtime、三写法、Native DOM、Native CSS Profile 已成为正式规范 |
| 2026-03-07 | 范围扩展回填 | 将 Skia 高级外观、文本字体、多语言、表单、表格、伪类、Tween 动画、图片九宫格纳入批准范围 |
| 2026-03-07 | 文档治理更新 | 不再新增 `v1` / `v2` 后缀 UI 文档，统一在现有文档上维护变更历史 |
