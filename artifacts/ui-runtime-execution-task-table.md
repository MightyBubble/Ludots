# UI Runtime Execution Task Table

Date: 2026-03-08
Status: In Progress
Scope: `Ludots.UI` / Native DOM / Native CSS Profile / Skia Rendering / Forms / Tables / Showcase / Skin Swap / Transition / Animation
Type: UI-only execution artifact

## 1. 总结

- 统一 UI Runtime 已落地：`UiScene`、`UiNode`、`UiDocument`、`UiSceneDiff` 已成为唯一主路径。
- 统一解析链已落地：`AngleSharp` 解析 HTML/DOM，`ExCSS` 解析 CSS，`FlexLayoutSharp` 负责主布局，`SkiaSharp` 负责原生渲染。
- 官方写法已稳定：`Compose Fluent`、`Reactive Fluent`、`Markup + C# CodeBehind` 三种模式并行存在，且各自有独立 showcase。
- 换皮能力已落地：同一 DOM / `UiScene` 可挂不同 Theme / Skin Mod，已提供 Classic / Sci-Fi / Paper 演示。
- 旧 UI 主路径已退出：不再以旧 `IUiSystem` / 旧字符串 UI 载荷作为主运行时。
- This batch now closes sibling selectors, logical pseudos, `nth-last-child`, `z-index`, `transform`, wrap, blur/backdrop blur, `text-align`, `object-fit`, `image-slice`, `transition`, `@keyframes` animation shorthand, scroll/clip, form-validation baseline, table baseline, background images, SVG image paths, and native `<canvas>`.
- 2026-03-08 verification: `UiRuntimeTests` 52 passed, `UiShowcaseTests` 15 passed, the capture tool passed, and key screenshots were manually spot-checked.
- Remaining execution scope is now Phase 6-7: the Phase 5 native animation baseline is complete; media bridges and Ludots component elements are next.

## 2. 完成度快照

### 2.1 统计口径

- 基线能力：11 / 11 已完成。
- Approved expansion scope: 14 items total, 12 Done, 2 Partial, 0 Planned.
- Weighted completion across approved expansion scope: about 93% (`Done=1`, `Partial=0.5`).
- 按“基线 + 已批准扩展”合并口径计算：约 96%。

### 2.2 当前剩余的 4 类差距

| 项目 | 当前状态 | 剩余差距 |
|------|----------|----------|
| 文本复杂 shaping / 浏览器级 BiDi | Partial | 已支持字体注册、换行、RTL 方向与基础视觉排序；未接入 HarfBuzz 级 shaping |
| Form 浏览器语义 | Partial | 已支持输入、checkbox、radio、select、textarea、`required/invalid`、radio group 必填校验、`email/pattern/minlength/maxlength/min/max/step`；未做浏览器完整 constraint validation、输入法语义与提交生命周期 |
| Table 浏览器语义 | Partial | 已支持 `table` / `thead` / `tbody` / `tfoot` / `tr` / `td` / `th` 原生节点、内容感知列宽、`colspan` / `rowspan` 与行组布局；未做浏览器完整 auto table layout |
| Web Overlay 高保真视觉对齐 | Partial | 统一消费 `UiSceneDiff` 已打通；Skia 特性如 blur/backdrop/nine-slice/完整过渡保真仍以原生路径为准 |

### 2.3 下一阶段实施顺序

| 阶段 | 状态 | 范围 | 主要代码落点 | 本阶段验收 |
|------|------|------|--------------|------------|
| Phase 1 | Done | 选择器扩展、盒模型与绘制顺序基础 | `src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs`、`src/Libraries/Ludots.UI/Runtime/UiSelectorMatcher.cs`、`src/Libraries/Ludots.UI/Runtime/UiElementSelectorMatcher.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyle.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`、`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs` | parser / matcher / render 测试通过，三种 Showcase 已补 selector / stacking / transform / clip focused crop |
| Phase 2 | Done | 多重背景、多重阴影、`border-style`、`mask`、`clip-path` | `src/Libraries/Ludots.UI/Runtime/UiStyle.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`、`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`、`src/Tests/UiRuntimeTests/UiRenderingTests.cs`、`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs`、`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` | AppearancePage 已补 Phase 2 visual lab 与 focused crop，证据见 `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png`、`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png`、`artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png` |
| Phase 3 | Partial | 文本排版增强基线 | `src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs`、`src/Libraries/Ludots.UI/Runtime/UiFontRegistry.cs`、`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` | 已完成 glyph fallback、`text-overflow: ellipsis`、`text-decoration` 与三种 Showcase Phase 3 截图；复杂 shaping / 浏览器级 BiDi 仍在剩余差距中 |
| Phase 4 | Done | 图片、背景图、SVG、原生 Canvas 节点 | `src/Libraries/Ludots.UI/Runtime/UiImageSourceCache.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`、`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/MarkupBinder.cs`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs` | `img` / `background-*` / SVG / Canvas 已进入三种 Showcase，证据见 `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`、`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`、`artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png` |
| Phase 5 | Done | `@keyframes` / `animation` shorthand 动画基线已与 tween 数学对齐 | `src/Libraries/Ludots.UI/Runtime/UiAnimationEntry.cs`, `src/Libraries/Ludots.UI/Runtime/UiAnimationRuntime.cs`, `src/Libraries/Ludots.UI/Runtime/UiNode.cs`, `src/Libraries/Ludots.UI/Runtime/UiStyle.cs`, `src/Libraries/Ludots.UI/Runtime/UiStyleSheet.cs`, `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`, `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs` | start / mid / end 截图与 trace 证据已覆盖三种官方写法；`animation-*` longhand 明确不纳入当前交付边界 |
说明：Phase 5 的正式交付边界是 `@keyframes` + `animation` shorthand。`animation-*` longhand 目前明确为未支持，不视为 Phase 5 尾项。
| Phase 6 | Planned | `video` / `audio` 与 HTML 控件桥接 | `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`、`src/Libraries/Ludots.UI/Runtime/UiNode.cs`、`src/Libraries/Ludots.UI/Runtime/UiScene.cs` | 仅做 C# 宿主桥接，不做浏览器媒体栈复刻；Showcase 必须可交互 |
| Phase 7 | Planned | Ludots Component Element、scoped style、slot-like child projection | `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`、`src/Libraries/Ludots.UI/Runtime/UiNode.cs`、`src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`、`src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs` | 以同一 DOM 换皮与自定义标签 fixture 证明组件化不破坏现有主路径 |

阶段约束：

- 不引入 JS，不做 `iframe`，不复刻完整浏览器 Canvas API。
- `video` / `audio` 只走 C# 宿主桥接，不走浏览器媒体语义。
- `svg` 优先评估 `Svg.Skia` 路径；`canvas` 优先做原生 C# + `SkiaSharp` 节点。
- `Custom Element` 只评估 Ludots 组件元素，不承诺浏览器 Shadow DOM / Web Components 标准兼容。

### 2.4 第一阶段工作包（已完成）

| 任务 | 状态 | 说明 |
|------|------|------|
| Selector grammar 扩展 | Done | `A + B`、`A ~ B`、`:not()`、`:is()`、`:where()`、`:nth-last-child()`、属性操作符 `^=` / `$=` / `*=` / `~=` / `|=` |
| Selector matching 对齐 | Done | 兄弟节点遍历、逆向结构伪类、specificity 与 style resolve 对齐 |
| Box model / stack order 基础 | Done | `z-index`、transform 数据模型、裁剪区域抽象、命中测试与渲染顺序一致 |
| CSS parser / style model 升级 | Done | `UiCssParser` rule merge 顺序、`UiStyle`、`UiStyleResolver` 已为多层绘制模型打好基线 |
| Tests / showcase 对齐 | Done | `src/Tests/UiRuntimeTests/UiDomAndCssTests.cs`、`src/Tests/UiRuntimeTests/UiRenderingTests.cs`、`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs` 与三种 AppearancePage phase1 focused crop 已同步到位 |

## 3. 基线能力完成情况

| 项目 | 状态 | 证据 |
|------|------|------|
| Runtime scene tree | Done | `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| Native DOM | Done | `src/Libraries/Ludots.UI/Runtime/UiDocument.cs` |
| CSS computed style | Done | `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| Flex primary layout | Done | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` |
| Skia base rendering | Done | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` |
| Compose Fluent | Done | `src/Libraries/Ludots.UI/Compose/` |
| Reactive Fluent | Done | `src/Libraries/Ludots.UI/Reactive/` |
| Markup + CodeBehind | Done | `src/Libraries/Ludots.UI.HtmlEngine/Markup/` |
| Skin / Theme swap | Done | `mods/UiSkinShowcaseMod/` |
| Runtime tests | Done | `src/Tests/UiRuntimeTests/` |
| Showcase tests | Done | `src/Tests/UiShowcaseTests/` |

## 4. 已批准扩展范围状态

| 项目 | 状态 | 证据 / 说明 |
|------|------|-------------|
| Advanced Skia appearance | Done | `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`，支持 blur / backdrop blur / 渐变 / 描边 / 阴影 |
| Text / fonts / multilingual / RTL | Partial | `src/Libraries/Ludots.UI/Runtime/UiTextLayout.cs`、`src/Libraries/Ludots.UI/Runtime/UiFontRegistry.cs`，支持字体 glyph fallback、换行、`direction`、`text-align`、`text-overflow: ellipsis`、`text-decoration`；不承诺浏览器级 shaping |
| Advanced Flex (`wrap` / `align-content` / `gap`) | Done | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs` 与 `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs` |
| Runtime pseudo flow (`focus`) | Done | `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| Advanced selector grammar | Done | `src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs`、`src/Libraries/Ludots.UI/Runtime/UiSelectorMatcher.cs`、`src/Libraries/Ludots.UI/Runtime/UiElementSelectorMatcher.cs`；支持 sibling combinator、logical pseudo、`nth-last-child`、属性操作符扩展 |
| Stack order / transform baseline | Done | `src/Libraries/Ludots.UI/Runtime/UiStyle.cs`、`src/Libraries/Ludots.UI/Runtime/UiTransform.cs`、`src/Libraries/Ludots.UI/Runtime/UiScene.cs`、`src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs`；支持 `z-index`、translate / rotate / scale、render / hit 对齐、diff 序列化 |
| Form semantics baseline | Done | `src/Libraries/Ludots.UI/Runtime/UiScene.cs`、`src/Libraries/Ludots.UI/Runtime/UiSelectorParser.cs`、`src/Tests/UiRuntimeTests/UiDomAndCssTests.cs`、`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs` |
| Table semantics baseline | Done | `src/Libraries/Ludots.UI/Runtime/UiLayoutEngine.cs`、`src/Libraries/Ludots.UI.HtmlEngine/Markup/UiMarkupLoader.cs`、`src/Tests/UiRuntimeTests/UiDomAndCssTests.cs`、`src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs` |
| Advanced image features | Done | `src/Libraries/Ludots.UI/Runtime/UiImageSourceCache.cs` 与 `src/Libraries/Ludots.UI/Runtime/UiSceneRenderer.cs` |
| Tween transition runtime | Done | `src/Libraries/Ludots.UI/Runtime/UiTransitionMath.cs` 与 `src/Libraries/Ludots.UI/Runtime/UiNode.cs` |
| CSS keyframe animation baseline | Done | `src/Libraries/Ludots.UI/Runtime/UiAnimationEntry.cs`, `src/Libraries/Ludots.UI/Runtime/UiAnimationRuntime.cs`, `src/Libraries/Ludots.UI/Runtime/UiStyleResolver.cs`, `src/Libraries/Ludots.UI.HtmlEngine/Markup/UiCssParser.cs`; supports `@keyframes` and `animation` shorthand while longhands still use the shorthand entry point |
| Clipping / scroll containers | Done | `src/Libraries/Ludots.UI/Runtime/UiScrollGeometry.cs` 与 `src/Libraries/Ludots.UI/Runtime/UiScene.cs` |
| Separate showcase modes + skin swap | Done | `mods/UiComposeShowcaseMod/`、`mods/UiReactiveShowcaseMod/`、`mods/UiMarkupShowcaseMod/`、`mods/UiSkinShowcaseMod/` |
| Web Overlay parity | Partial | `src/Client/Web/src/rendering/UiOverlay.ts` 与 `src/Client/Web/src/core/FrameDecoder.ts` |

## 5. 最新验收命令与结果

| 命令 | 结果 | 日期 |
|------|------|------|
| `dotnet test src/Tests/UiRuntimeTests/UiRuntimeTests.csproj -v minimal --no-restore` | 52 Passed | 2026-03-08 |
| `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj -v minimal --no-restore` | 15 Passed | 2026-03-08 |
| `dotnet run --project src/Tools/Ludots.UI.ShowcaseCapture/Ludots.UI.ShowcaseCapture.csproj -v minimal` | Passed | 2026-03-08 |

## 6. Latest Visible Acceptance Artifacts

### 6.1 Compose
- Initial: `artifacts/acceptance/ui-showcase-compose/screens/compose-initial.png`
- Forms: `artifacts/acceptance/ui-showcase-compose/screens/compose-forms.png`
- Appearance: `artifacts/acceptance/ui-showcase-compose/screens/compose-appearance.png`
- Transition: `artifacts/acceptance/ui-showcase-compose/screens/compose-transition.png`
- Phase 1: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase1.png`
- Phase 2: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png`
- Phase 3: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase3.png`
- Phase 4: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`
- Phase 5 Start: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-start.png`
- Phase 5 Mid: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-mid.png`
- Phase 5 End: `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-end.png`
- Scroll: `artifacts/acceptance/ui-showcase-compose/screens/compose-scroll.png`
- Selection: `artifacts/acceptance/ui-showcase-compose/screens/compose-selection.png`
- Table: `artifacts/acceptance/ui-showcase-compose/screens/compose-table.png`
- Modal: `artifacts/acceptance/ui-showcase-compose/screens/compose-modal.png`

### 6.2 Reactive
- Initial: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-initial.png`
- Forms: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-forms.png`
- Appearance: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-appearance.png`
- Transition: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-transition.png`
- Phase 1: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase1.png`
- Phase 2: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png`
- Phase 3: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase3.png`
- Phase 4: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`
- Phase 5 Start: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-start.png`
- Phase 5 Mid: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-mid.png`
- Phase 5 End: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-end.png`
- Scroll: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-scroll.png`
- Table: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-table.png`
- Counter: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-counter.png`
- Modal: `artifacts/acceptance/ui-showcase-reactive/screens/reactive-modal.png`

### 6.3 Markup
- Initial: `artifacts/acceptance/ui-showcase-markup/screens/markup-initial.png`
- Forms: `artifacts/acceptance/ui-showcase-markup/screens/markup-forms.png`
- Appearance: `artifacts/acceptance/ui-showcase-markup/screens/markup-appearance.png`
- Transition: `artifacts/acceptance/ui-showcase-markup/screens/markup-transition.png`
- Phase 1: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase1.png`
- Phase 2: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png`
- Phase 3: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase3.png`
- Phase 4: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png`
- Phase 5 Start: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-start.png`
- Phase 5 Mid: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-mid.png`
- Phase 5 End: `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-end.png`
- Scroll: `artifacts/acceptance/ui-showcase-markup/screens/markup-scroll.png`
- Table: `artifacts/acceptance/ui-showcase-markup/screens/markup-table.png`
- Counter: `artifacts/acceptance/ui-showcase-markup/screens/markup-counter.png`
- Modal: `artifacts/acceptance/ui-showcase-markup/screens/markup-modal.png`

### 6.4 Skin And Parity
- Skin Swap: `artifacts/acceptance/ui-showcase-skin-swap/`
- Style Parity: `artifacts/acceptance/ui-showcase-style-parity/`

## 7. Manual Spot Check
2026-03-08 reviewed screenshots:
- `artifacts/acceptance/ui-showcase-compose/screens/compose-initial.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-transition.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase1.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase3.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-start.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-end.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-scroll.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-forms.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-table.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-selection.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-initial.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase1.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase3.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-start.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-end.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-transition.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-forms.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-table.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-initial.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase1.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase3.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-start.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-end.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-scroll.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-forms.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-table.png`
Conclusions:
- transition probes remain visible, and focus-state outline / color changes are visible.
- scroll host content and scrollbar position changes are visible.
- selector / stacking focused crops still prove sibling selectors, stacking, transform rotation, and hit alignment.
- form `:invalid`, selection, counter, and modal interaction states remain visible.
- Phase 5 tri-frames clearly show background-color, opacity, blur, and backdrop blur intermediate states with overlayed current style values.
- the three official authoring modes continue to provide visible, interactive, and regression-friendly showcase evidence.

## 8. UI SSOT
- Support matrix: `docs/reference/ui_native_css_html_support_matrix.md`
- ADR: `docs/adr/ADR-0002-ui-runtime-unification.md`
- RFC: `docs/rfcs/RFC-0001-ui-runtime-fluent-authoring.md`
- Demo mod plan: `artifacts/ui-runtime-demo-mod-plan.md`
- Demo acceptance plan: `artifacts/ui-runtime-demo-acceptance-plan.md`

## 9. Change History
| Date | Change | Notes |
|------|--------|-------|
| 2026-03-07 | Establish execution table | Unified the Runtime / DOM / CSS / Flex / authoring / Showcase baseline. |
| 2026-03-07 | Expand approved scope | Added appearance, fonts, multilingual, form, table, image, transition, scroll, and skin-swap goals. |
| 2026-03-08 | Backfill scroll / clip | Wheel input, drag, scrollbar, hit-testing, clipping, and screenshot evidence are in place. |
| 2026-03-08 | Backfill text / image / transition | `direction`, `text-align`, `object-fit`, `image-slice`, and `transition` entered the execution table with acceptance evidence. |
| 2026-03-08 | Backfill focused screenshot capture | The capture tool now crops below-the-fold capability blocks so human review can see them. |
| 2026-03-08 | Backfill form / table baseline semantics | `:required`, `:invalid`, radio-group required validation, `email/pattern/minlength/maxlength/min/max/step`, `colspan/rowspan`, content-aware columns, and Markup CSS parity are accepted. |
| 2026-03-08 | Finish Phase 1 selector / stacking / transform | Advanced selector grammar, `z-index`, transform hit/render alignment, three-mode phase1 focused crops, and Markup CSS parity are accepted. |
| 2026-03-08 | Freeze later implementation order | Selector / box-model work became Phase 1; visual, text, image, animation, media-bridge, and component-element work remain sequenced after it. |
| 2026-03-08 | Backfill forms / table focused crop | Three official authoring modes now include forms / table screenshots plus manual spot checks. |
| 2026-03-08 | Backfill Phase 3 text baseline | glyph fallback, `text-overflow: ellipsis`, `text-decoration`, and three-mode Phase 3 focused crops are accepted. |
| 2026-03-08 | Backfill Phase 4 image / SVG / Canvas | `background-image:url(...)`, `background-size`, `background-position`, `background-repeat`, SVG image rendering / inline import, and native C# `<canvas>` Phase 4 focused crops are accepted. |
| 2026-03-08 | 收口 Phase 5 动画基线 | `@keyframes`、`animation` shorthand、direction / fill-mode / play-state / `infinite` 与 start / mid / end tri-frame 验收闭环完成；`animation-*` longhand 明确不计入 Phase 5 尾项 |
