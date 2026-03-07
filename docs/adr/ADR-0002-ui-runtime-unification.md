# ADR-0002 统一 UI Runtime、Native DOM 与三前端写法

本记录定义 Ludots UI 的正式收敛结果：仓库已经移除旧的 HTML 字符串 UI 契约与 `UIRoot.Content` 业务入口，统一到一个原生 C# UI Runtime，以 Native DOM + Native CSS Profile 作为结构与样式中间层，并在其上提供三种官方 authoring 写法。

## 1 背景

在迁移前，仓库存在 HTML 字符串 UI、实验性 Reactive/Widget UI 和 Web 字符串传输三套并行路径。自 2026-03-07 起，这些历史分叉已经完成收敛：

- Core 旧契约 `IUiSystem.SetHtml(string html, string css)` 与 `ShowUiCommand` 已移除。
- `UIRoot.Content` 已移除，`UIRoot` 只保留 `MountScene(UiScene)` 场景挂载边界。
- Web 适配层已切到 `UiSceneDiff` 主载荷，不再以 `html/css` 字符串作为 UI 真相。
- `AngleSharp` + `ExCSS` + `FlexLayoutSharp` 已纳入正式主线：分别承担 DOM 解析、CSS 解析和主布局计算。

## 2 决策

采用如下统一方向，并以仓库实现为准：

- 只保留一个 UI Runtime 作为唯一真相层。
- 在统一 Runtime 内使用 Native DOM：`UiDocument` / `UiElement` 作为统一文档结构层，供 Compose、Reactive、Markup 三种写法共享。
- 在统一 Runtime 内使用 Native CSS Profile：负责 selector、cascade、specificity、inheritance、variables、layout style 与 theme token 收敛。
- 在该 Runtime 上提供三种官方写法：
  - Compose Fluent：Flutter-like、链式调用、纯 C#。
  - Reactive Fluent：React-like、组件式、纯 C#。
  - Markup + C# CodeBehind：HTML/CSS 负责结构与样式，事件、绑定和动态逻辑全部用 C#。
- 不引入模板 DSL、表达式小语言和 JS 脚本层。
- Web、Raylib 和后续平台只实现宿主与渲染适配，不承载独立 UI 语义。

## 2.1 文档治理约束

- UI 架构与能力文档保持固定路径，不再用 `v1` / `v2` / `phase2` 后缀复制出并行 SSOT。
- 后续能力扩展直接回填现有 ADR / RFC / 支持矩阵 / 执行任务表，并记录变更历史。
- 文档中的 `Supported` / `Done` 只能描述仓库中已有实现；待交付能力统一记录在 `artifacts/ui-runtime-execution-task-table.md`。

## 3 当前实现状态

当前仓库中的正式实现路径如下：

- Runtime: `src/Libraries/Ludots.UI/Runtime/UiScene.cs`、`src/Libraries/Ludots.UI/Runtime/UiNode.cs`
- DOM: `src/Libraries/Ludots.UI/Runtime/UiDocument.cs`、`src/Libraries/Ludots.UI/Runtime/UiElement.cs`
- CSS Resolve: `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`
- Layout: `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`
- Compose: `src/Libraries/Ludots.UI/Compose/`
- Reactive: `src/Libraries/Ludots.UI/Reactive/`
- Markup: `src/Libraries/Ludots.UI.HtmlEngine/Markup/`
- Web SceneDiff: `src/Adapters/Web/Ludots.Adapter.Web/Services/WebUiSystem.cs`

## 4 官方推荐场景

- Compose Fluent：主游戏 UI、HUD、性能敏感界面的默认推荐路径。
- Reactive Fluent：工具型、状态驱动型、复杂列表和编辑器类 UI。
- Markup + CodeBehind：设计原型导入、内容型页面、主题型页面和富文本型 UI。

## 5 对 Mod 作者的约束

- 不再允许业务 Mod 直接写 `UIRoot.Content`。
- 不再允许业务路径继续扩展 `SetHtml(string html, string css)`。
- 设计原型通过 Markup + Native DOM 导入，动态行为写在 C# CodeBehind / Reactive / Compose 中。

## 6 相关文档

- RFC：`docs/rfcs/RFC-0001-ui-runtime-fluent-authoring.md`
- 支持矩阵：`docs/reference/ui_native_css_html_support_matrix.md`
- 执行任务表：`artifacts/ui-runtime-execution-task-table.md`

## 7 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | ADR 建立 | 正式收敛 UI Runtime、Native DOM、Native CSS Profile 与三种官方写法 |
| 2026-03-07 | 文档治理回填 | 明确 UI 文档不再使用 `v1` / `v2` 后缀，统一在现有 SSOT 中维护历史 |
