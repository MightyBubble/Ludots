# UI Runtime Demo 验收计划

Date: 2026-03-07
Status: Implemented baseline, expansion planned
Scope: 官方 UI Showcase Demo 可见验收、交互验收、截图产物、路径记录
Type: UI-only planning artifact

## 1 目标

本计划定义 UI Demo 的验收方式，确保 Demo 不只是“能跑起来”，而是“可见、可交互、可截图、可回归”。

## 2 当前验收链路

### 2.1 自动化与可见产物

- Runtime 测试：`src/Tests/UiRuntimeTests/`
- Showcase 验收测试：`src/Tests/UiShowcaseTests/`
- 截图工具：`src/Tools/Ludots.UI.ShowcaseCapture/Program.cs`

### 2.2 当前产物目录

- `artifacts/acceptance/ui-showcase-compose/`
- `artifacts/acceptance/ui-showcase-reactive/`
- `artifacts/acceptance/ui-showcase-markup/`
- `artifacts/acceptance/ui-showcase-style-parity/`
- `artifacts/acceptance/ui-showcase-skin-swap/`

每个目录当前都要求包含：

- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `visible-checklist.md`
- `screens/`

## 3 当前基线验收目标

| 场景 | 状态 | 说明 |
|------|------|------|
| Compose Showcase | Implemented | 已产出初始页、弹窗态、选择态截图 |
| Reactive Showcase | Implemented | 已产出状态变化与弹窗截图 |
| Markup Showcase | Implemented | 已产出原型导入与交互截图 |
| Style Parity | Implemented | 已产出样式一致性基线产物 |
| Skin Swap | Implemented | 已产出 Classic / SciFi / Paper 换肤截图 |

## 4 已批准扩展验收范围

以下能力进入交付时，必须同步补上对应 Demo 产物与可见验收：

- 高级外观：阴影、渐变、描边、模糊、毛玻璃
- 文本与字体：换行、自定义字体、多语言、RTL/BiDi、复杂字形
- 动画：Tween 驱动的悬停、按下、显隐、主题切换过渡
- 表单：焦点链、radio、校验态、组合表单
- 表格：表头、行列布局、选中态、滚动态
- 图片：九宫格、裁剪、拉伸、皮肤图切换

## 5 验收规则

- 每新增一类官方能力，必须至少补一页独立 Showcase
- 每页至少产出一个可见检查清单和一组截图
- 交互型能力必须同时产出 `trace.jsonl` 与 `path.mmd`
- 视觉差异型能力必须覆盖至少一种皮肤切换或主题切换场景

## 6 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立 Demo 验收计划 | 固化 Runtime / Showcase / Capture / 产物目录规则 |
| 2026-03-07 | 编码修复 | 将历史乱码计划文档改写为 UTF-8 可读文档 |
| 2026-03-07 | 范围扩展回填 | 将高级外观、文本字体、动画、表单、表格、图片九宫格纳入后续验收要求 |
