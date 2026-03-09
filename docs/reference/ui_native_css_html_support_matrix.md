# UI Native CSS / HTML 支持矩阵

Date: 2026-03-08
Status: Active
Scope: `Ludots.UI` 统一运行时、原生 DOM、原生 CSS Profile、Skia 渲染、Markup 导入

## 1. 定位

本矩阵描述的是 Ludots 当前**已经实现并可验收**的 UI 能力边界。

- 这是一套原生 C# UI 运行时，不是浏览器级 `CSS3+ / HTML5 / JS` 引擎。
- 官方开发入口只有三种：`Compose Fluent`、`Reactive Fluent`、`Markup + C# CodeBehind`。
- 不引入 JS，不引入 DSL，不做模板脚本层；动态行为统一由 C# 驱动。
- DOM、CSS、布局、渲染统一汇聚到 `UiScene` / `UiSceneDiff`，不再以宿主字符串 HTML 作为主运行时。
- 多平台口径统一，但**视觉保真以原生 Skia 路径为当前验收基线**；Web Overlay 走同一份 `UiSceneDiff`，但不是浏览器完整兼容层。
- 本矩阵只描述当前已支持边界；未支持项的实施顺序与阶段状态统一维护在 `artifacts/ui-runtime-execution-task-table.md`。

## 2. 已复用基建

| 基建 | 状态 | 用途 |
|------|------|------|
| `AngleSharp` | In Use | HTML 解析为 DOM |
| `ExCSS` | In Use | CSS 规则与内联样式解析 |
| `FlexLayoutSharp` | In Use | 主布局计算 |
| `SkiaSharp` | In Use | 原生 2D 绘制、截图导出、滤镜、图片绘制 |
| `UiScene` / `UiSceneDiff` | In Use | 统一运行时场景树、事件、Diff、宿主输出 |

关键实现路径：

- DOM / Scene：`src/Libraries/Ludots.UI/Runtime/UiDocument.cs`
- Selector：`src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs`
- Selector Match：`src/Libraries/Ludots.UI/Runtime/UiSelectorMatcher.cs`
- CSS Resolve：`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`
- Layout：`src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`
- Text：`src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs`
- Render：`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`
- Animation: `src/Libraries/Ludots.UI/Runtime/UiAnimationRuntime.cs`
- Image Cache：`src/Libraries/Ludots.UI/Runtime/UiImageSourceCache.cs`
- Markup 导入：`src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`

## 3. 官方开发模式与推荐场景

| 模式 | 官方写法 | 推荐场景 | 说明 |
|------|----------|----------|------|
| Compose | Fluent 链式 C# | HUD、菜单、稳定业务界面 | 类型明确、结构清晰、性能稳定 |
| Reactive | 状态驱动 Fluent C# | 工具面板、编辑器、状态密集 UI | 适合频繁状态切换和局部重组 |
| Markup | HTML/CSS + C# CodeBehind | 原型导入、内容页、设计稿对齐 | 保留 DOM/CSS 资产形态，但运行时仍是纯 C# |
| Skin Mod | 同一 DOM + 不同 Theme/Mod | 换皮、主题包、多平台风格切换 | 结构不变，仅替换样式包或皮肤资源 |

硬约束：

- 不支持 JS。
- 不支持自定义 DSL。
- 允许 Fluent 链式写法，但运行时逻辑必须是 C#。
- `Markup` 只负责结构与样式导入，交互仍由 C# CodeBehind 绑定。

## 4. CSS 选择器支持矩阵

### 4.1 已支持

| 类别 | 写法 | 状态 | 说明 |
|------|------|------|------|
| 通配选择器 | `*` | Supported | 任意节点匹配 |
| 标签选择器 | `div` `button` | Supported | 按 `TagName` 匹配 |
| ID 选择器 | `#root` | Supported | 按 `id` / `elementId` 匹配 |
| Class 选择器 | `.panel` | Supported | 支持多 class |
| 选择器列表 | `button, .primary` | Supported | 多选择器规则 |
| 后代选择器 | `.panel .title` | Supported | 已实现 |
| 子代选择器 | `.panel > .title` | Supported | 已实现 |
| 相邻兄弟选择器 | `A + B` | Supported | 已实现 adjacency sibling matching |
| 通用兄弟选择器 | `A ~ B` | Supported | 已实现 general sibling matching |
| 属性存在选择器 | `[disabled]` | Supported | 按属性存在匹配 |
| 属性等值选择器 | `[type=checkbox]` | Supported | 按属性值匹配 |
| 属性操作符选择器 | `[data-role^=hero]` `[data-tone$=cold]` `[data-flags*=tag]` `[data-flags~=tag]` `[lang|=zh]` | Supported | 已支持 `^=` / `$=` / `*=` / `~=` / `|=` |
| 根伪类 | `:root` | Supported | 根节点 |
| 运行时伪类 | `:hover` `:active` `:focus` `:disabled` `:checked` `:selected` `:required` `:invalid` | Supported | 由 `UiScene` 维护运行时状态与基础表单校验态 |
| 逻辑伪类 | `:not()` `:is()` `:where()` | Supported | 已实现 logical pseudo matching；`:where()` 走零 specificity 语义 |
| 结构伪类 | `:first-child` `:last-child` `:nth-child(...)` | Supported | 支持整数、`odd`、`even`、`an+b` |
| 逆向结构伪类 | `:nth-last-child(...)` | Supported | 支持整数、`odd`、`even`、`an+b` |

### 4.2 未支持

| 类别 | 写法 | 状态 | 说明 |
|------|------|------|------|
| 关系型伪类 | `:has()` | Not Supported | 未实现 |
| 伪元素 | `::before` `::after` | Not Supported | 未实现 |

## 5. CSS 属性支持矩阵

### 5.1 布局与盒模型

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `display` | Supported | `flex`、`block`、`inline`、`text`、`none` |
| `flex-direction` | Supported | `row`、`column` |
| `justify-content` | Supported | `start`、`center`、`end`、`space-between`、`space-around`、`space-evenly` |
| `align-items` | Supported | `start`、`center`、`end`、`stretch` |
| `align-content` | Supported | `start`、`center`、`end`、`stretch`、`space-between`、`space-around`、`space-evenly` |
| `flex-wrap` | Supported | `nowrap`、`wrap`、`wrap-reverse` |
| `flex-grow` / `flex-shrink` / `flex-basis` | Supported | 已接入布局计算 |
| `gap` / `row-gap` / `column-gap` | Supported | 原生 Profile 已实现；不承诺浏览器级细枝末节完全一致 |
| `position` | Supported | `relative`、`absolute` |
| `left` / `top` / `right` / `bottom` | Supported | 长度值与百分比 |
| `width` / `height` / `min-*` / `max-*` | Supported | `px`、`%`、`auto` |
| `margin` / `padding` | Supported | 1~4 值写法 |
| `overflow` | Supported | `visible`、`hidden`、`clip`、`scroll` |
| `clip-content` / `overflow-clip` | Supported | 原生裁剪开关 |
| `grid` | Not Supported | 不做 CSS Grid |
| `z-index` | Supported | 已接入 stacking order、渲染排序与命中测试优先级 |
| `transform` | Supported | 基线支持 `translate` / `translate%`、`rotate`、`scale`，并与 render / hit test 对齐 |
| `transform-origin` / 3D transform | Not Supported | 暂未实现 `transform-origin`、3D transform、matrix / skew 等浏览器扩展 |

### 5.2 视觉外观

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `background-color` | Supported | 纯色背景 |
| `background` | Supported | 纯色或逗号分隔的 `linear-gradient(...)` 图层 |
| `background-image` | Supported | 支持 `linear-gradient(...)` 与 `url(...)` 资源背景图层 |
| `border-width` / `border-color` | Supported | 基础描边 |
| `border-radius` | Supported | 单一圆角半径 |
| `outline` / `outline-width` / `outline-color` | Supported | 基础外轮廓 |
| `box-shadow` | Supported | 支持单层 / 多层逗号分隔阴影 |
| `filter` | Supported | 仅 `blur(...)` |
| `backdrop-filter` | Supported | 仅 `blur(...)` |
| `opacity` | Supported | `0..1` |
| `visibility` | Supported | `visible` / `hidden` |
| 毛玻璃 / blur / 渐变 / 描边 | Supported | 走原生 Skia 路径 |
| 多重背景 / 多重阴影 | Supported | 基础多层绘制模型已接通，支持逗号分隔 `linear-gradient(...)` 与 `box-shadow` |
| `border-style` | Supported | 支持 `solid` / `dashed` / `dotted` |
| `mask` / `clip-path` | Partial | 支持 `mask-image: linear-gradient(...)`，`clip-path: circle(...)` / `inset(...)`；未覆盖浏览器完整形状模型 |

### 5.3 文本与字体

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `color` | Supported | 文本颜色 |
| `font-size` | Supported | 浮点数 / `px` |
| `font-family` | Supported | 接入 `UiFontRegistry`，并支持按 glyph 选择可显示字体 |
| `font-weight` | Supported | 基础 `bold` 判定 |
| `white-space` | Supported | `normal`、`nowrap`、`pre-wrap` |
| `text-overflow` | Supported | `ellipsis` |
| `text-shadow` | Supported | 单层阴影 |
| `direction` | Supported | `ltr`、`rtl`、`auto` |
| `text-align` | Supported | `left`、`right`、`center`、`start`、`end` |
| 文本换行 | Supported | 原生换行计算 |
| 多语言文本 | Supported | 基础多语言字符显示 |
| RTL / BiDi | Partial | 支持 `direction`、`text-align`、基础 RTL 展示与 glyph fallback；不等同浏览器级 BiDi + shaping |
| 复杂字形整形 | Not Supported | 当前未接入 HarfBuzz；Arabic / Indic 等复杂 shaping 仍未纳入验收支持 |
| 文本装饰 | Supported | `underline`、`line-through` |

### 5.4 图片与资源

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `img` 节点渲染 | Supported | 原生图片绘制 |
| `object-fit` | Supported | `fill`、`contain`、`cover`、`none`、`scale-down` |
| `image-slice` / `nine-slice` / `border-image-slice` | Supported | 原生九宫格切片 |
| 图片固有尺寸参与布局 | Supported | 读取解码后图片尺寸 |
| 皮肤图片切换 | Supported | 与 Theme / Skin Mod 配合 |
| `background-size` | Supported | `auto`、`cover`、`contain` 与基础长度 / 百分比尺寸已接通，用于 `url(...)` 背景图层 |
| `background-position` | Supported | 支持 `left` / `center` / `right` / `top` / `bottom` 与基础长度 / 百分比偏移 |
| `background-repeat` | Supported | 支持 `repeat`、`repeat-x`、`repeat-y`、`no-repeat` |
| SVG 图片资源 | Partial | 通过 `Svg.Skia` 支持 `img src`、`background-image:url(...)` 与 inline `<svg>` 导入；不支持完整 SVG DOM / script / animation |
| `border-image` 完整语义 | Not Supported | 当前仅切片概念已接入；完整 `border-image` 语义仍未实现 |

### 5.5 动画与交互状态

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `transition` | Supported | shorthand parsing + runtime interpolation |
| easing | Supported | `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out` |
| animatable properties | Supported | `background-color`, `border-color`, `outline-color`, `color`, `opacity`, `filter` (blur only), `backdrop-filter` (blur only) |
| `@keyframes` | Supported | supports `from`, `to`, and `<percentage>` keyframes in the native runtime |
| `animation` shorthand | Supported | supports `name duration timing-function delay iteration-count direction fill-mode play-state` |
| animation runtime semantics | Supported | supports direction, fill-mode, play-state, and `infinite` iteration-count |
| `animation-*` longhand | Not Supported | use the `animation` shorthand entry point for now |
| JS event scripts | Not Supported | not supported |

> 说明：`longhand` 指拆开的独立属性，例如 `animation-name`、`animation-duration`；`shorthand` 指将多个子属性合并写在一条声明中，例如 `animation: pulse 1s ease-in-out 0.2s infinite;`。
>
> 当前 Ludots UI 正式支持的是 `animation` shorthand。`animation-*` longhand 明确记为 `Not Supported`；这不是 Phase 5 的遗留尾巴，Phase 5 的交付边界已经按 shorthand 入口关闭。

### 5.6 自定义属性

| 属性 / 能力 | 状态 | 当前支持 |
|-------------|------|----------|
| `--token` | Supported | 自定义属性 |
| `var(--token)` | Supported | 变量引用 |
| `var(--token, fallback)` | Supported | fallback 回退 |

### 5.7 表单 / 表格原生语义

| 能力 | 状态 | 当前支持 |
|------|------|----------|
| `required` / `:required` | Supported | `input`、`textarea`、`select`、radio group 基础必填态 |
| `invalid` / `:invalid` | Supported | 基于空值、checkbox/radio 选中态、radio group 必填态，以及 `email` / `pattern` / `minlength` / `maxlength` / `min` / `max` / `step` 的原生运行时校验 |
| `pattern` / `minlength` / `maxlength` | Supported | 非空输入会参与约束校验，并驱动 `:invalid` 与 `aria-invalid` |
| `min` / `max` / `step` | Supported | `type=number` / `type=range` 支持数值区间与步进校验 |
| email 基线格式校验 | Supported | `type=email` 具备原生运行时基线格式验证 |
| `value` / `placeholder` 渲染 | Supported | 输入类节点无 `TextContent` 时会回退渲染 `value` / `placeholder` |
| checkbox / radio 语义 | Supported | `checked` 切换、radio 互斥组、必填 radio group 校验 |
| `aria-required` / `aria-invalid` 同步 | Supported | 运行时会回填基础可访问性属性，稳定输出 `true/false` |
| 浏览器完整 constraint validation | Partial | 已支持 `email`、`pattern`、`minlength`、`maxlength`、`min/max`、`step`；未实现浏览器完整提交生命周期、IME 细节与宿主一致性 |
| table 内容感知列宽 | Supported | `table` / `thead` / `tbody` / `tfoot` / 直挂 `tr` 均支持按内容拟合列宽 |
| `colspan` / `rowspan` | Supported | 表格单元格支持跨列、跨行布局与内容驱动尺寸分摊 |
| 浏览器完整 table layout | Partial | 已支持 `colspan` / `rowspan`、内容感知列宽与行高分摊；未实现 `table-layout` 全语义与浏览器兼容细节 |

## 6. HTML 标签 / 控件支持矩阵

### 6.1 已支持或可导入

| 标签 / 控件 | 状态 | 运行时映射 | 说明 |
|-------------|------|------------|------|
| `div` `section` `main` `header` `footer` `nav` `aside` | Supported | `Container` | 通用容器 |
| `form` | Partial | `Container` | 支持结构导入；子控件支持 `required`、`:invalid`、`email` / `pattern` / `minlength` / `maxlength` / `min/max/step` 基线校验，但无浏览器原生提交生命周期 |
| `ul` `ol` `li` | Supported | `Container` | 列表结构导入 |
| `article` | Supported | `Card` | 语义卡片 |
| `span` `label` `p` `h1`~`h6` | Supported | `Text` | 文本节点 |
| `button` | Supported | `Button` | 与 C# 行为绑定交互 |
| `img` | Supported | `Image` | 原生图片节点 |
| `input[type=text|email|password|number]` | Supported | `Input` | 基础输入控件，支持 `value`、`placeholder`、`required`、`:invalid`、`pattern`、`minlength`、`maxlength`，以及 `number` 的 `min/max/step` |
| `input[type=checkbox]` | Supported | `Checkbox` | 支持 DOM、伪类、点击切换 |
| `input[type=radio]` | Supported | `Radio` | 支持互斥组行为 |
| `input[type=range]` | Supported | `Slider` | 基础滑条节点 |
| `input[type=button|submit|reset]` | Supported | `Button` | 导入为按钮语义 |
| `select` | Supported | `Select` | 基础选择控件，支持 `required`、`:invalid`、`aria-required/invalid` 同步 |
| `textarea` | Supported | `TextArea` | 多行输入控件，支持 `value`、`placeholder`、`required`、`:invalid`、`maxlength` |
| `table` `thead` `tbody` `tfoot` `tr` `td` `th` | Partial | `Table*` | 原生表格语义、内容感知列宽、行组布局、`colspan`、`rowspan` 已接通；仍不是浏览器完整 table layout |
| `canvas` | Supported | `Custom` | 原生 C# 画布节点，支持 `UiCanvasContent` 与 `ui-canvas` / `data-canvas` 绑定；不支持浏览器 Canvas API |
| `svg` | Partial | `Image` | 支持 SVG 图片资源渲染与 inline `<svg>` 导入；运行时按图像处理，不支持完整 SVG DOM / script / animation |
| 未专门映射的其它标签 | Partial | `Container` | 保留 `TagName`，按容器处理 |

### 6.2 未支持 / 非目标

| 标签 / 能力 | 状态 | 说明 |
|-------------|------|------|
| `script` | Not Supported | 不支持 JS |
| `template` | Not Supported | 不支持模板执行层 |
| 浏览器 Canvas API 语义 | Not Supported | `<canvas>` 只作为原生 C# 绘制宿主，不实现浏览器 2D / WebGL DOM API |
| 完整 SVG DOM / script / animation | Not Supported | 当前仅支持 SVG 作为图像资源或 inline import，不支持浏览器级 SVG DOM 模型 |
| `video` / `audio` | Not Supported | 当前无浏览器媒体语义；Phase 6 仅考虑 C# 宿主桥接 |
| `iframe` | Not Supported | 无嵌入浏览器模型 |
| `dialog` / `details` / `summary` | Not Supported | 无浏览器专属交互语义 |
| `input[type=file|date|time|color]` | Not Supported | 复杂宿主控件未实现；Phase 6 仅考虑宿主桥接方案 |
| `a` | Partial | 可导入结构与属性；无浏览器原生导航语义，后续只考虑 C# 导航桥接 |
| 浏览器完整表单校验 | Partial | 已支持 `required` / `:invalid`、`email`、`pattern`、`minlength`、`maxlength`、`min/max/step` 基线；未实现浏览器完整 constraint validation 与提交生命周期 |
| Shadow DOM / Custom Elements | Not Supported | 当前不做 Shadow DOM / Web Components 标准实现；Phase 7 仅评估 Ludots 组件元素、scoped style、slot-like child projection |

## 7. 宿主差异说明

| 宿主 | 状态 | 说明 |
|------|------|------|
| Raylib / Skia native path | Primary | visual acceptance baseline for blur, backdrop blur, nine-slice, scrollbar, transition, and animation |
| Web Overlay | Partial | 使用同一份 `UiSceneDiff`；已同步布局矩形、滚动偏移、`direction`、`text-align`、基础 `object-fit`、基础 flex 呈现，以及 `z-index` / transform 序列化字段 |
| Web Overlay high-fidelity visuals | Partial | full fidelity for `filter`, `backdrop-filter`, `nine-slice`, native scrollbar chrome, transition, and animation is not guaranteed to match Skia |

## 8. 当前验收证据

### 8.1 能力样板与截图索引

| 能力组 | 官方样板 | 资源 / 样式 SSOT | 截图证据 | 截图说明 |
|------|----------|------------------|----------|----------|
| 官方基线 / 页面结构 | `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Showcase/MarkupShowcaseCodeBehind.cs` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css` | `artifacts/acceptance/ui-showcase-compose/screens/compose-initial.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-initial.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-initial.png` | 首屏证明三种官方写法都能渲染 Overview / Controls / Forms / Collections / Overlays / Appearance，Markup 额外保留 PrototypeImportPage。 |
| 表单 / 校验 | `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `artifacts/acceptance/ui-showcase-compose/screens/compose-forms.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-forms.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-forms.png` | 可见 `required`、`pattern`、`password`、`textarea`、`:invalid` 红框与约束文案；Markup 证明 HTML/CSS 文件化资产已进入验收链路。 |
| 集合 / 表格 | `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `artifacts/acceptance/ui-showcase-compose/screens/compose-selection.png`；`artifacts/acceptance/ui-showcase-compose/screens/compose-table.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-table.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-table.png` | 证明选中态、内容感知列宽、`rowspan`、`colspan` 与统一集合语义可见且可复核。 |
| 外观 / 毛玻璃 / 图片基础 | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`；`mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css` | `artifacts/acceptance/ui-showcase-compose/screens/compose-appearance.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-appearance.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-appearance.png` | 证明 `filter`、`backdrop-filter`、`flex-wrap`、`object-fit` 与九宫格图片样板可见。 |
| Phase 4 图片 / SVG / Canvas | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`；`mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Showcase/MarkupShowcaseCodeBehind.cs` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html`；`mods/UiShowcaseCoreMod/Assets/Showcase/showcase_badge.svg` | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png` | 证明 `background-image:url(...)`、`background-size`、`background-position`、`background-repeat`、SVG 图像渲染 / inline import 与原生 C# `<canvas>` 节点在三种官方写法中都可见。 |
| Phase 3 文本实验 | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`；`mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css` | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase3.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase3.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-phase3.png` | 证明 CJK / RTL / emoji、`text-overflow: ellipsis`、`text-decoration`、字体 glyph fallback 可见；不在此行宣称复杂 shaping 已支持。 |
| 选择器 / 层叠 / 变换 | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css` | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase1.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase1.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-phase1.png` | 证明 `A + B`、`A ~ B`、`:not()`、`:is()`、`:where()`、`:nth-last-child()`、属性操作符扩展、`z-index` 与 `transform` 聚焦可见。 |
| 滚动 / 裁剪 | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css` | `artifacts/acceptance/ui-showcase-compose/screens/compose-scroll.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-scroll.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-scroll.png` | 证明 `overflow: scroll`、clip host、滚轮输入与滚动偏移更新可见。 |
| 过渡 / Tween 基线 | `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`；`mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`；`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`；`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` | `artifacts/acceptance/ui-showcase-compose/screens/compose-transition.png`；`artifacts/acceptance/ui-showcase-reactive/screens/reactive-transition.png`；`artifacts/acceptance/ui-showcase-markup/screens/markup-transition.png` | 截图工具通过 `Advance(0.16s)` 固化中间帧，证明原生 tween / transition 插值可见且可回归。 |
| Phase 5 native animation | `mods/UiShowcaseCoreMod/Showcase/UiShowcaseScaffolding.cs`; `mods/UiShowcaseCoreMod/Showcase/ComposeShowcaseController.cs`; `mods/UiShowcaseCoreMod/Showcase/ReactiveShowcasePageFactory.cs`; `mods/UiShowcaseCoreMod/Showcase/MarkupShowcaseCodeBehind.cs` | `mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`; `mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html`; `src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-start.png`; `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-mid.png`; `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-end.png`; `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-start.png`; `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-mid.png`; `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-end.png`; `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-start.png`; `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-mid.png`; `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-end.png` | proves `@keyframes` + `animation` shorthand with start / mid / end tri-frames; screenshots overlay current style values for manual spot check |
| 换皮 / 样式一致性 | `mods/UiShowcaseCoreMod/Showcase/UiSkinShowcaseSceneFactory.cs`；`mods/UiShowcaseCoreMod/Showcase/UiSkinThemes.cs`；`mods/UiShowcaseCoreMod/Showcase/UiShowcaseFactory.cs` | `mods/UiSkinClassicMod/`；`mods/UiSkinSciFiHudMod/`；`mods/UiSkinPaperMod/` | `artifacts/acceptance/ui-showcase-skin-swap/screens/skin-classic.png`；`artifacts/acceptance/ui-showcase-skin-swap/screens/skin-scifi.png`；`artifacts/acceptance/ui-showcase-skin-swap/screens/skin-paper.png`；`artifacts/acceptance/ui-showcase-style-parity/screens/parity-compose.png`；`artifacts/acceptance/ui-showcase-style-parity/screens/parity-reactive.png`；`artifacts/acceptance/ui-showcase-style-parity/screens/parity-markup.png` | 证明同一 DOM / `UiScene` 可切不同皮肤，且三种官方写法共享一套视觉语义。 |

### 8.2 自动化与产物入口

- Runtime 测试：`src/Tests/UiRuntimeTests/UiDomAndCssTests.cs`
- 渲染测试：`src/Tests/UiRuntimeTests/UiRenderingTests.cs`
- Showcase 测试：`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`
- 截图工具：`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs`
- Compose 产物：`artifacts/acceptance/ui-showcase-compose/`
- Reactive 产物：`artifacts/acceptance/ui-showcase-reactive/`
- Markup 产物：`artifacts/acceptance/ui-showcase-markup/`
- Skin Swap 产物：`artifacts/acceptance/ui-showcase-skin-swap/`
- Style Parity 产物：`artifacts/acceptance/ui-showcase-style-parity/`
## 9. 结论

Ludots 当前已经具备一套**完整可用的原生 C# UI 框架主路径**：

- 结构层：原生 DOM + 原生 CSS Profile + Markup 导入
- 编程层：Compose、Reactive、Markup + CodeBehind 三种官方写法
- 渲染层：Skia 原生渲染，多平台通过 `UiScene` / `UiSceneDiff` 统一适配
- 换皮层：同一 DOM 可挂不同 Skin Mod / Theme 包

但它**不是**浏览器级 `CSS3+ / HTML5 / JS` 引擎；当前未支持的核心大项主要集中在：

- JS / CSSOM / 浏览器默认事件模型
- CSS Grid / browser Canvas API / full SVG DOM
- 浏览器完整表格布局与表单校验

## 10. 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立统一矩阵 | 统一 Runtime / DOM / CSS / Flex / Markup 的支持口径 |
| 2026-03-07 | 取消版本后缀文档策略 | UI SSOT 不再拆 `v1` / `v2` |
| 2026-03-08 | 回填 scroll / clip | `overflow: scroll`、裁剪、滚轮、拖拽、滚动条验收同步到位 |
| 2026-03-08 | 回填结构伪类与外观增强 | `:first-child`、`:last-child`、`:nth-child(...)`、`flex-wrap`、`align-content`、`filter`、`backdrop-filter` |
| 2026-03-08 | 完成 Phase 1 selector / stacking / transform 基线 | `A + B`、`A ~ B`、`:not()`、`:is()`、`:where()`、`:nth-last-child()`、属性操作符扩展、`z-index`、`transform` 进入统一矩阵 |
| 2026-03-08 | 回填文本 / 图片 / 过渡能力 | `direction`、`text-align`、`object-fit`、`image-slice`、`transition` 纳入统一矩阵 |
| 2026-03-08 | 回填表单 / 表格基线语义 | `:required`、`:invalid`、radio group 必填校验、内容感知表格列宽纳入统一矩阵 |
| 2026-03-08 | 明确未支持项执行入口 | 未支持项的实施顺序统一转入 `artifacts/ui-runtime-execution-task-table.md` 维护 |
| 2026-03-08 | 资源化 Markup 样板并补齐 forms / table 截图索引 | markup_showcase.html、markup_showcase.css、showcase_authoring.css 成为仓库内 SSOT，支持矩阵新增样板路径与截图说明 |
| 2026-03-08 | 回填 Phase 3 文本实验 | `text-overflow: ellipsis`、`text-decoration`、glyph fallback 与三种模式 Phase 3 截图证据纳入统一矩阵 |
| 2026-03-08 | 回填 Phase 4 图片 / SVG / Canvas | `background-image:url(...)`、`background-size`、`background-position`、`background-repeat`、SVG 图像路径、原生 `<canvas>` 节点与三种模式截图证据纳入统一矩阵 |
| 2026-03-08 | 回填 Phase 5 动画基线 | `@keyframes`、`animation` shorthand、direction / fill-mode / play-state / `infinite` 与三种写法 tri-frame 证据已纳入矩阵；`animation-*` longhand 明确标记为未支持 |
