# UI Runtime Demo 验收计划

Date: 2026-03-08
Status: Executed and passing
Scope: 官方 UI Showcase Demo 的可见验收、交互验收、截图产物、trace、path
Type: UI-only planning artifact

## 1. 目标

确保 UI demo 不只是“能跑起来”，而是“可见、可交互、可截图、可回归、可人工复核”。

## 2. 当前验收链路

| 环节 | 工具 / 位置 | 当前状态 |
|------|-------------|----------|
| Runtime 语义测试 | `src/Tests/UiRuntimeTests/` | Passed |
| Showcase 语义测试 | `src/Tests/UiShowcaseTests/` | Passed |
| 截图采集 | `src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` | Passed |
| Battle report / trace / path | `artifacts/acceptance/ui-showcase-*/` | Generated |
| 人工 spot check | 2026-03-08 本轮执行 | Completed |

## 3. 最新自动化结果

| 命令 | 结果 |
|------|------|
| `dotnet test src/Tests/UiRuntimeTests/UiRuntimeTests.csproj -v minimal --no-restore` | 52 Passed |
| `dotnet test src/Tests/UiShowcaseTests/UiShowcaseTests.csproj -v minimal --no-restore` | 15 Passed |
| `dotnet run --project src/Tools/Ludots.UI.ShowcaseCapture/Ludots.UI.ShowcaseCapture.csproj -v minimal` | Passed |

## 4. 产物目录约定

每个 acceptance 目录都必须包含：

- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `visible-checklist.md`
- `screens/`

当前目录：

- `artifacts/acceptance/ui-showcase-compose/`
- `artifacts/acceptance/ui-showcase-reactive/`
- `artifacts/acceptance/ui-showcase-markup/`
- `artifacts/acceptance/ui-showcase-style-parity/`
- `artifacts/acceptance/ui-showcase-skin-swap/`

## 5. 当前必验场景

### 5.1 Compose

| 场景 | 产物 |
|------|------|
| 全景首屏 | `artifacts/acceptance/ui-showcase-compose/screens/compose-initial.png` |
| Forms 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-forms.png` |
| Appearance 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-appearance.png` |
| Transition 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-transition.png` |
| Phase 1 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase1.png` |
| Phase 2 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png` |
| Phase 3 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase3.png` |
| Phase 4 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png` |
| Phase 5 Start | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-start.png` |
| Phase 5 Mid | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-mid.png` |
| Phase 5 End | `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-end.png` |
| Scroll 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-scroll.png` |
| Selection 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-selection.png` |
| Table 聚焦 | `artifacts/acceptance/ui-showcase-compose/screens/compose-table.png` |
| Modal | `artifacts/acceptance/ui-showcase-compose/screens/compose-modal.png` |

### 5.2 Reactive

| 场景 | 产物 |
|------|------|
| 全景首屏 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-initial.png` |
| Forms 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-forms.png` |
| Appearance 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-appearance.png` |
| Transition 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-transition.png` |
| Phase 1 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase1.png` |
| Phase 2 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png` |
| Phase 3 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase3.png` |
| Phase 4 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png` |
| Phase 5 Start | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-start.png` |
| Phase 5 Mid | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-mid.png` |
| Phase 5 End | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-end.png` |
| Scroll 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-scroll.png` |
| Table 聚焦 | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-table.png` |
| Counter | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-counter.png` |
| Modal | `artifacts/acceptance/ui-showcase-reactive/screens/reactive-modal.png` |

### 5.3 Markup

| 场景 | 产物 |
|------|------|
| 全景首屏 | `artifacts/acceptance/ui-showcase-markup/screens/markup-initial.png` |
| Forms 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-forms.png` |
| Appearance 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-appearance.png` |
| Transition 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-transition.png` |
| Phase 1 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase1.png` |
| Phase 2 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png` |
| Phase 3 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase3.png` |
| Phase 4 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png` |
| Phase 5 Start | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-start.png` |
| Phase 5 Mid | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-mid.png` |
| Phase 5 End | `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-end.png` |
| Scroll 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-scroll.png` |
| Table 聚焦 | `artifacts/acceptance/ui-showcase-markup/screens/markup-table.png` |
| Counter | `artifacts/acceptance/ui-showcase-markup/screens/markup-counter.png` |
| Modal | `artifacts/acceptance/ui-showcase-markup/screens/markup-modal.png` |

### 5.4 皮肤与一致性

| 场景 | 产物 |
|------|------|
| Style parity | `artifacts/acceptance/ui-showcase-style-parity/` |
| Skin swap | `artifacts/acceptance/ui-showcase-skin-swap/` |

## 6. 本轮人工复核内容

已人工查看：

- `artifacts/acceptance/ui-showcase-compose/screens/compose-transition.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase1.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-start.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase5-end.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase3.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-scroll.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-forms.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-table.png`
- `artifacts/acceptance/ui-showcase-compose/screens/compose-selection.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-initial.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase1.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-start.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase5-end.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase3.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-transition.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-forms.png`
- `artifacts/acceptance/ui-showcase-reactive/screens/reactive-table.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-initial.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase1.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-start.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-mid.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase5-end.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase3.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-scroll.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-forms.png`
- `artifacts/acceptance/ui-showcase-markup/screens/markup-table.png`
人工结论：

- 不再只有首屏静态图；transition / scroll / selection / forms / table 等关键状态现在肉眼可见。
- selector / stack focused crop 已纳入正式验收口径，三种写法都能肉眼看到 sibling selector 与 transform stacking 结果。
- Compose / Reactive / Markup 首屏均可见表单 `:invalid` 红框、约束校验文案与官方标题文案，无乱码。
- Compose / Reactive / Markup 的 forms focused crop 已可见 `:invalid` 红框、`required` / `pattern` / `minlength` / `maxlength` 校验状态，且 Markup 证明外置 HTML/CSS 资产已进入正式验收。
- Compose / Reactive / Markup 的 table focused crop 已可见表格列宽差异与 `colspan/rowspan` 布局，不再出现黑底裁图伪验收。
- Compose / Reactive / Markup 的 Phase 3 focused crop 已可见 CJK / RTL / emoji、`ellipsis` 与 `underline + line-through`；复杂 shaping / 浏览器级 BiDi 仍保持未完成口径。
- Compose / Reactive / Markup 的 Phase 4 focused crop 已可见 `background-image:url(...)`、SVG 图像渲染 / inline import 与原生 C# `<canvas>` 节点。
- 针对首屏下方能力块，截图工具已改为 focused crop，不会再出现“产物存在但看不到变化”的伪验收。
- Compose / Reactive / Markup 三种写法都已经具备可见、可交互、可回归的演示证据。
## 7. 验收规则

- 每补一类官方能力，至少补一组截图、battle report、trace、path。
- 交互能力必须有 `trace.jsonl` 与 `path.mmd`。
- 如果能力块在首屏下方，允许使用 focused crop，但必须在 `battle-report.md` 中明确说明。
- 任何“状态已变化但截图肉眼不可见”的场景都不得视为通过。

## 8. 下一阶段增量验收

| 阶段 | 必补自动化 | 必补可见验收 |
|------|------------|--------------|
| Phase 1（已完成） | selector parser / matcher 测试、`z-index` / transform / clip 渲染测试 | 三种写法的 AppearancePage 已新增 focused crop，并证明 sibling selector 与 stacking 生效 |
| Phase 2（已完成） | 多重背景 / 阴影 / `mask` / `clip-path` 渲染测试 | 已补 `artifacts/acceptance/ui-showcase-compose/screens/compose-phase2.png`、`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase2.png`、`artifacts/acceptance/ui-showcase-markup/screens/markup-phase2.png`，可见 layered surface / dotted border / mask / clip-path 差异 |
| Phase 3（部分完成） | 文本排版 / glyph fallback / ellipsis / decoration 测试 | 已补 `compose-phase3.png`、`reactive-phase3.png`、`markup-phase3.png`；复杂 shaping / 浏览器级 BiDi 继续保留为非阻断项 |
| Phase 4（已完成） | `img` / `background-*` / SVG / Canvas fixture 测试 | 已补 `artifacts/acceptance/ui-showcase-compose/screens/compose-phase4.png`、`artifacts/acceptance/ui-showcase-reactive/screens/reactive-phase4.png`、`artifacts/acceptance/ui-showcase-markup/screens/markup-phase4.png`，可见 CSS 背景图、SVG 图像路径与原生 Canvas 节点 |
| Phase 5 | keyframe / animation runtime 测试 | 每个动画样例至少保留初始 / 中间 / 结束帧截图与 trace |
| Phase 6 | 媒体桥接与控件桥接 stub 测试 | `video` / `audio` / bridged input 的可见与可交互示例必须落地 |
| Phase 7 | custom element / scoped style / slot-like fixture 测试 | 组件标签在三种写法与 skin swap 中都要有截图与 DOM 结构证据 |

阶段验收规则：

- 每进入下一阶段，必须先更新本验收计划，再落代码。
- 截图工具 `src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` 必须同步升级，不能靠人工截图补洞。
- 新能力如果需要 focused crop，必须在 `battle-report.md` 与 `visible-checklist.md` 中明确记录。

## 9. 当前剩余非阻断项

- 复杂字形 shaping / 浏览器级 BiDi
- 浏览器完整表单校验态
- 浏览器完整表格自动布局
- Web Overlay 与原生 Skia 的高保真视觉对齐

## 10. 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立 Demo 验收计划 | 固化 Runtime / Showcase / Capture / 产物目录规则 |
| 2026-03-08 | 回填 appearance / image / transition / scroll 验收 | 三种写法均补齐对应截图与 trace |
| 2026-03-08 | 回填人工 spot check 规则 | focused crop 进入正式验收口径，避免首屏外能力块漏检 |
| 2026-03-08 | 回填 Phase 1 focused crop 验收 | selector / stack focused crop、battle report、trace、path 已纳入 compose / reactive / markup 三条链路 |
| 2026-03-08 | 回填 form / table 验收 | 首屏表单 `:invalid`、Compose 表格列宽与乱码修复已纳入人工复核 |
| 2026-03-08 | 冻结阶段性增量验收 | Phase 1~7 的自动化与截图验收口径进入正式计划 |
| 2026-03-08 | 回填 forms / table focused crop | 三种写法新增 forms / table 截图、battle report、visible checklist 与人工 spot check |
| 2026-03-08 | 回填 Phase 3 文本验收 | 三种写法新增 Phase 3 focused crop，人工 spot check 明确区分“已完成文本基线”和“未完成复杂 shaping” |
| 2026-03-08 | 回填 Phase 4 图片 / SVG / Canvas 验收 | 三种写法新增 Phase 4 focused crop，自动化结果更新为 `50 Passed` / `14 Passed`，visible checklist 与截图链路同步完成 |
| 2026-03-08 | 收口 Phase 5 动画验收 | 三种写法的 start / mid / end tri-frame、gate、`52 Passed` / `15 Passed` 已同步回填；`animation-*` longhand 明确为未支持且不计入 Phase 5 尾项 |
