# UI Native CSS / HTML 支持矩阵

Date: 2026-03-07
Status: Active
Scope: Ludots 统一 UI Runtime、Native DOM、Native CSS Profile、Markup 导入

## 1 范围说明

本矩阵描述当前仓库**实际已实现**的 UI 能力，不描述浏览器标准的理论上限。

文档治理约束：

- UI SSOT 文档不再追加 `v1` / `v2` / `phase2` 之类版本后缀。
- 能力扩展直接回填到现有 SSOT 文档，并在文末维护变更历史。
- 已批准但尚未交付的扩展范围统一记录在 `artifacts/ui-runtime-execution-task-table.md`，本矩阵只陈述**当前实际支持**。

边界原则：

- 这是应用级 Native CSS / HTML Profile，不是浏览器级 CSS3+/HTML5/JS 引擎。
- UI 动态行为只支持 C#，不支持 JS、模板 DSL、表达式 DSL。
- Web / Raylib 统一消费 `UiScene` / `UiSceneDiff`，不再使用 `html/css` 字符串主载荷。
- 主布局后端已统一切到 `FlexLayoutSharp`；`AngleSharp` 负责 HTML/DOM，`ExCSS` 负责 CSS 解析。
- 已批准的后续扩展方向包括：Skia 高级外观、文本与字体系统、多语言排版、表单/表格语义、结构伪类、Tween 动画与图片九宫格能力；这些能力尚未在本矩阵中标记为 Supported 前，不得视为已交付。

关键实现路径：

- Runtime: `src/Libraries/Ludots.UI/Runtime/UiScene.cs`
- DOM: `src/Libraries/Ludots.UI/Runtime/UiDocument.cs`
- CSS Resolve: `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`
- Layout: `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`
- Text Layout: `src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs`
- Font Registry: `src/Libraries/Ludots.UI/Runtime/UiFontRegistry.cs`
- Skia Render: `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`
- Markup Import: `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`
- CSS Parse: `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs`

## 2 CSS 选择器支持


| 类别        | 写法                                                             | 状态            | 说明                |
| --------- | -------------------------------------------------------------- | ------------- | ----------------- |
| 标签选择器     | `div` `button`                                                 | Supported     | 按 `TagName` 匹配    |
| ID 选择器    | `#root`                                                        | Supported     | 按 `elementId` 匹配  |
| Class 选择器 | `.panel`                                                       | Supported     | 支持多 class         |
| 选择器列表     | `button, .primary`                                             | Supported     | 通过 `ParseMany` 支持 |
| 后代选择器     | `.panel .title`                                                | Supported     | 已实现               |
| 子代选择器     | `.panel > .title`                                              | Supported     | 已实现               |
| 属性存在选择器   | `[disabled]`                                                   | Supported     | 按属性存在匹配           |
| 属性等值选择器   | `[type=checkbox]`                                              | Supported     | 按属性等值匹配           |
| `:root`   | `:root`                                                        | Supported     | 仅根节点              |
| 运行时伪状态    | `:hover` `:active` `:focus` `:disabled` `:checked` `:selected` | Supported     | 依赖 Runtime 状态     |
| 兄弟选择器     | `+` `~`                                                        | Not Supported | 未实现               |
| 结构伪类      | `:first-child` `:nth-child()`                                  | Not Supported | 未实现               |
| 否定/关系     | `:not()` `:has()`                                              | Not Supported | 未实现               |
| 伪元素       | `::before` `::after`                                           | Not Supported | 未实现               |


## 3 CSS 属性支持

### 3.1 已支持


| 属性                                  | 状态        | 说明                                                                             |
| ----------------------------------- | --------- | ------------------------------------------------------------------------------ |
| `display`                           | Supported | `flex` / `none` / `text`；`block` / `inline` 为 Native Runtime 语义映射              |
| `flex-direction`                    | Supported | `row` / `column`                                                               |
| `justify-content`                   | Supported | `start` / `center` / `end` / `space-between` / `space-around` / `space-evenly` |
| `align-items`                       | Supported | `start` / `center` / `end` / `stretch`                                         |
| `position`                          | Supported | `relative` / `absolute`                                                        |
| `left` / `top` / `right` / `bottom` | Supported | `px` / `%`                                                                     |
| `width` / `height`                  | Supported | `px` / `%`                                                                     |
| `min-width` / `min-height`          | Supported | `px` / `%`                                                                     |
| `max-width` / `max-height`          | Supported | `px` / `%`                                                                     |
| `flex-basis`                        | Supported | `px` / `%` / `auto`                                                            |
| `flex-grow` / `flex-shrink`         | Supported | 数值                                                                             |
| `gap`                               | Supported | 数值/像素，映射为主轴间距                                                                  |
| `margin` / `padding`                | Supported | 1-4 值 thickness                                                                |
| `border-width`                      | Supported | 单值                                                                             |
| `border-radius`                     | Supported | 单值                                                                             |
| `background` / `background-color`   | Supported | 支持纯色背景；`background` 也可解析 `linear-gradient(...)`                              |
| `background-image`                  | Supported | 当前支持 `linear-gradient(...)`                                                        |
| `border-color`                      | Supported | 颜色                                                                             |
| `box-shadow`                        | Supported | 当前支持单层阴影：`offset-x offset-y blur color` / 可带 spread                         |
| `text-shadow`                       | Supported | 当前支持单层文本阴影                                                                   |
| `outline` / `outline-width` / `outline-color` | Supported | 当前支持基础描边语义                                                           |
| `color`                             | Supported | 颜色                                                                             |
| `font-size`                         | Supported | 数值/像素                                                                          |
| `font-family`                       | Supported | 支持系统字体族与 `UiFontRegistry.RegisterFile(...)` 注册字体                           |
| `font-weight`                       | Supported | `bold` / 数值粗体语义                                                                |
| `white-space`                       | Supported | `normal` / `nowrap` / `pre-wrap`                                                    |
| `opacity`                           | Supported | `0..1`                                                                         |
| `visibility`                        | Supported | `visible` / `hidden`                                                           |
| `overflow`                          | Supported | `visible` / `hidden` / `scroll` / `clip`                                       |
| `clip-content` / `overflow-clip`    | Supported | Runtime 布尔语义                                                                   |
| `--token`                           | Supported | 自定义属性                                                                          |
| `var(--token)`                      | Supported | 变量引用                                                                           |
| `var(--token, fallback)`            | Supported | 带 fallback                                                                     |


### 3.2 已支持的继承


| 属性            | 状态        | 说明   |
| ------------- | --------- | ---- |
| `color`       | Supported | 基础继承 |
| `font-size`   | Supported | 基础继承 |
| `font-weight` | Supported | 基础继承 |


### 3.3 未支持


| 属性/能力                            | 状态            | 说明         |
| -------------------------------- | ------------- | ---------- |
| `z-index`                        | Not Supported | 未实现层叠上下文   |
| `transform`                      | Not Supported | 未实现        |
| `filter` / `backdrop-filter`     | Not Supported | 未实现        |
| `line-height` / `letter-spacing` | Not Supported | 未实现        |
| `border-style`                   | Not Supported | 当前仅宽度+颜色   |
| `cursor`                         | Not Supported | 未实现        |
| `object-fit`                     | Not Supported | 未实现        |
| CSS Grid                         | Not Supported | 未实现        |
| Animation / Transition           | Not Supported | 未实现        |
| `calc()`                         | Not Supported | 未实现        |
| Media Query                      | Not Supported | 未实现        |
| CSSOM                            | Not Supported | 未实现        |


## 4 Runtime 控件种类支持


| 控件/节点        | 状态        |
| ------------ | --------- |
| `Container`  | Supported |
| `Text`       | Supported |
| `Button`     | Supported |
| `Image`      | Supported |
| `Panel`      | Supported |
| `Row`        | Supported |
| `Column`     | Supported |
| `Input`      | Supported |
| `Checkbox`   | Supported |
| `Radio`      | Supported |
| `Toggle`     | Supported |
| `Slider`     | Supported |
| `Select`     | Supported |
| `TextArea`   | Supported |
| `ScrollView` | Supported |
| `List`       | Supported |
| `Card`       | Supported |
| `Table`      | Supported |
| `TableHeader`| Supported |
| `TableBody`  | Supported |
| `TableFooter`| Supported |
| `TableRow`   | Supported |
| `TableCell`  | Supported |
| `TableHeaderCell` | Supported |
| `Custom`     | Supported |


## 5 HTML 标签映射

### 5.1 已支持 / 可导入


| HTML 标签                                                            | 状态        | Runtime 映射  | 说明                     |
| ------------------------------------------------------------------ | --------- | ----------- | ---------------------- |
| `div` / `section` / `main` / `header` / `footer` / `nav` / `aside` | Supported | `Container` | 通用容器                   |
| `form`                                                             | Supported | `Container` | 仅结构导入，无浏览器表单提交语义       |
| `ul` / `ol` / `li`                                                 | Supported | `Container` | 仅结构导入                  |
| `article`                                                          | Supported | `Card`      | 语义卡片                   |
| `span` / `label` / `p` / `h1` ~ `h6`                               | Supported | `Text`      | 文本节点                   |
| `button`                                                           | Supported | `Button`    | 可通过 C# CodeBehind 绑定点击 |
| `img`                                                              | Supported | `Image`     | 基础图片节点                 |
| `input[type=text                                                   | email     | password    | number                 |
| `input[type=checkbox]`                                             | Supported | `Checkbox`  | DOM / pseudo-state / style matching plus click toggle |
| `input[type=radio]`                                                | Supported | `Radio`     | DOM / pseudo-state / exclusive-group semantics |
| `input[type=range]`                                                | Supported | `Slider`    | 基础滑条节点                 |
| `input[type=button                                                 | submit    | reset]`     | Supported              |
| `select`                                                           | Supported | `Select`    | 基础选择节点                 |
| `textarea`                                                         | Supported | `TextArea`  | 基础多行输入节点               |
| `table` / `thead` / `tbody` / `tfoot` / `tr` / `td` / `th`         | Supported | `Table*`    | Baseline native table / row / cell semantics; not full browser table algorithm |
| 未专门映射的其他标签                                                         | Partial   | `Container` | 保留 `TagName`，按容器处理     |


### 5.2 未支持 / 非目标


| HTML 标签/能力                                       | 状态            | 说明                      |
| ------------------------------------------------ | ------------- | ----------------------- |
| `script`                                         | Not Supported | 不支持 JS                  |
| `template`                                       | Not Supported | 不支持模板执行层                |
| `canvas`                                         | Not Supported | 无浏览器画布语义                |
| `svg`                                            | Not Supported | 无 SVG DOM / 绘制栈         |
| `video` / `audio`                                | Not Supported | 无浏览器媒体语义                |
| `iframe`                                         | Not Supported | 无嵌入浏览器模型                |
| `a`                                              | Partial       | 可导入，但无原生导航语义            |
| `dialog` / `details` / `summary`                 | Not Supported | 无浏览器专有交互语义              |
| `input[type=radio]`                              | Partial       | 当前仍映射为通用 `Input`        |
| 浏览器表单校验                                          | Not Supported | 需用 C# 业务校验              |
| 浏览器默认事件模型                                        | Not Supported | 统一走 Ludots Runtime 事件模型 |


## 6 官方推荐使用方式


| 场景                   | 推荐写法                   | 原因             |
| -------------------- | ---------------------- | -------------- |
| HUD / 菜单 / 战斗 UI     | Compose Fluent         | 类型明确、性能稳定、链式好写 |
| 工具面板 / 编辑器 / 状态密集 UI | Reactive Fluent        | 状态驱动重组更清晰      |
| 原型导入 / 内容页面 / 主题页面   | Markup + C# CodeBehind | 更接近设计稿和原型资产    |

## 7 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立统一矩阵 | 统一 Runtime / DOM / CSS / Flex / Markup 的当前支持口径 |
| 2026-03-07 | 移除版本尾缀策略 | 不再使用 `v1` / `v2` 作为 UI SSOT 文档命名或范围后缀 |
| 2026-03-07 | 扩展范围回填规则 | 已批准但未交付的能力改由 `artifacts/ui-runtime-execution-task-table.md` 持续维护 |
