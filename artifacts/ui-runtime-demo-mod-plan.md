# UI Runtime Demo Mod 计划

Date: 2026-03-08
Status: Implemented and accepted for current native profile
Scope: 官方 UI Showcase Mod 拆分、三种开发模式展示、同 DOM 换皮展示
Type: UI-only planning artifact

## 1. 目标

本计划定义 Ludots 官方 UI Demo Mod 的拆分原则，避免把所有演示逻辑塞进一个“大而全”的 demo 包里。

目标如下：

- `Compose`、`Reactive`、`Markup` 三种官方写法各自有独立 showcase。
- `Hub` 只负责入口与导航，不承载全部示例实现。
- “同一 DOM / 不同皮肤”的能力由独立 skin showcase 演示。
- 共享脚手架只放在 `UiShowcaseCoreMod`，不向上污染宿主层。
- 每种写法都必须产出自己的截图、trace、battle report、visible checklist。

## 2. 当前模块拓扑

| Mod | 状态 | 作用 |
|-----|------|------|
| `mods/UiShowcaseCoreMod/` | Implemented | 共享工厂、共享样式、共享脚手架、图片资源 |
| `mods/UiShowcaseHubMod/` | Implemented | UI Showcase 总入口与导航 |
| `mods/UiComposeShowcaseMod/` | Implemented | Compose 官方写法演示 |
| `mods/UiReactiveShowcaseMod/` | Implemented | Reactive 官方写法演示 |
| `mods/UiMarkupShowcaseMod/` | Implemented | Markup + CodeBehind 官方写法演示 |
| `mods/UiSkinShowcaseMod/` | Implemented | 同一 DOM 切不同皮肤 |
| `mods/UiDomSkinFixtureMod/` | Implemented | 共享 DOM / fixture |
| `mods/UiSkinClassicMod/` | Implemented | Classic 皮肤 |
| `mods/UiSkinSciFiHudMod/` | Implemented | Sci-Fi HUD 皮肤 |
| `mods/UiSkinPaperMod/` | Implemented | Paper 皮肤 |

## 3. 每种写法当前覆盖面

### 3.1 Compose

- `OverviewPage`
- `ControlsPage`
- `FormsPage`
- `CollectionsPage`
- `OverlaysPage`
- `AppearancePage`
- appearance blocks: blur, frosted glass, wrap, advanced selector, stacking / transform, RTL, multilingual, image, transition, animation, scroll/clip, Phase 2 visual lab, Phase 3 text lab, Phase 4 image / SVG / Canvas lab, Phase 5 animation lab

### 3.2 Reactive

- `OverviewPage`
- `ControlsPage`
- `FormsPage`
- `CollectionsPage`
- `OverlaysPage`
- `AppearancePage`
- appearance blocks: blur, frosted glass, wrap, advanced selector, stacking / transform, RTL, multilingual, image, transition, animation, scroll/clip, Phase 2 visual lab, Phase 3 text lab, Phase 4 image / SVG / Canvas lab, Phase 5 animation lab
- 额外状态变更：counter、theme switch

### 3.3 Markup

- `OverviewPage`
- `ControlsPage`
- `FormsPage`
- `CollectionsPage`
- `OverlaysPage`
- `AppearancePage`
- `PrototypeImportPage`
- appearance blocks: blur, frosted glass, wrap, advanced selector, stacking / transform, RTL, multilingual, image, transition, animation, scroll/clip, Phase 2 visual lab, Phase 3 text lab, Phase 4 image / SVG / Canvas lab, Phase 5 animation lab
- 全部交互仍走 C# CodeBehind，不引入 JS
- 结构 / 样式资源已外置到 `mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.html`、`mods/UiShowcaseCoreMod/Assets/Showcase/markup_showcase.css`、`mods/UiShowcaseCoreMod/Assets/Showcase/showcase_authoring.css`

### 3.4 Skin Swap

- 同一 DOM Hash 保持稳定
- Theme / Skin Mod 切换 Classic、Sci-Fi、Paper
- 样式变化通过 `UiScene` / `UiThemePack` 生效，而不是克隆另一套 DOM

## 4. 模块边界规则

- `UiShowcaseCoreMod` 只放共享构件、共享样式、共享资源，不放入口导航。
- `UiShowcaseHubMod` 只负责入口和聚合，不承载具体页面实现。
- 三种官方写法各自维护自己的装配逻辑与交互入口。
- 皮肤演示只能通过同一 DOM 挂不同主题包完成，不能复制三套页面“伪装成换皮”。
- 新增 showcase 能力优先补到三种官方写法共享脚手架，除非该能力天然只属于某一写法。

## 5. 当前验收证据

| 维度 | 证据 |
|------|------|
| Compose | `artifacts/acceptance/ui-showcase-compose/` |
| Reactive | `artifacts/acceptance/ui-showcase-reactive/` |
| Markup | `artifacts/acceptance/ui-showcase-markup/` |
| Skin Swap | `artifacts/acceptance/ui-showcase-skin-swap/` |
| Style Parity | `artifacts/acceptance/ui-showcase-style-parity/` |
| Markup HTML/CSS 资产 | `mods/UiShowcaseCoreMod/Assets/Showcase/` |
| Showcase tests | `src/Tests/UiShowcaseTests/UiShowcaseAcceptanceTests.cs` |
| Capture tool | `src/Tools/Ludots.UI.ShowcaseCapture/Program.cs` |

## 6. 与最初拆分要求的对齐结论

| 要求 | 当前结论 |
|------|----------|
| 三种开发模式单独拆 showcase | 已完成 |
| 不做一个大而全的唯一 demo mod | 已完成 |
| 同一 DOM / 不同皮肤能力可演示 | 已完成 |
| Markup 保留 DOM / CSS 原型导入能力 | 已完成 |
| 交互逻辑坚持 C#，不引入 JS / DSL | 已完成 |

## 7. 下一阶段 Showcase 对齐路线

| 阶段 | 三种官方写法共同义务 | 额外约束 |
|------|----------------------|----------|
| Phase 1（已完成） | `AppearancePage` 已补 sibling selector、stacking、transform、clip 基线示例，并纳入三种 focused crop | 共享 fixture 只能落在 `mods/UiShowcaseCoreMod/`，Hub 只负责导航 |
| Phase 2（已完成） | `AppearancePage` 已补多重背景、多重阴影、`mask`、`clip-path` 可见示例 | 共享 `BuildPhaseTwoPanel(...)` + 文件化 `markup_showcase.html` / `showcase_authoring.css` 已让三种写法展示同一视觉语义 |
| Phase 3（已落地文本基线） | 已补共享文本实验块，覆盖 CJK / RTL demo / ellipsis / decoration / glyph fallback | `Markup` 保留 DOM / CSS 原型导入，交互仍走 C#；复杂 shaping 继续保留为运行时边界 |
| Phase 4（已完成） | 已补图片 / 背景图 / SVG / Canvas 实验块 | 共享脚手架与文件化 `markup_showcase.html` / `showcase_authoring.css` 已让三种写法同时展示 CSS 背景图、SVG 图像路径与原生 C# Canvas 节点，并生成 `compose-phase4.png`、`reactive-phase4.png`、`markup-phase4.png` |
| Phase 5（已完成） | `@keyframes` / `animation` shorthand 动画实验块已落地 | 共享脚手架与文件化 `markup_showcase.html` / `showcase_authoring.css` 已产出 `compose-phase5-start.png` / `compose-phase5-mid.png` / `compose-phase5-end.png`、`reactive-phase5-start.png` / `reactive-phase5-mid.png` / `reactive-phase5-end.png`、`markup-phase5-start.png` / `markup-phase5-mid.png` / `markup-phase5-end.png`；`animation-*` longhand 不作为官方 Showcase 写法 |
说明：Showcase 对 Phase 5 的官方演示入口统一采用 `animation` shorthand。Demo Mod 不额外提供 `animation-*` longhand 变体，以免制造错误官方写法。
| Phase 6 | 补 `video` / `audio` / `a` / `progress` / `dialog` / bridged input 控件展示 | 媒体与宿主控件全部走 C# 桥接，不引入 JS |
| Phase 7 | 补自定义标签 / 组件元素 fixture，并验证同 DOM 换皮不破坏组件树 | 仅做 Ludots 组件元素，不承诺 Shadow DOM 标准兼容 |

Showcase 约束：

- 每推进一个阶段，都必须同时更新 `Compose`、`Reactive`、`Markup` 三种官方写法。
- 共享能力块优先进入 `mods/UiShowcaseCoreMod/` 脚手架，再由三种写法装配。
- Skin Swap 必须复用同一 DOM / fixture，不允许复制页面做“伪换皮”。
- 新增页面或能力块必须同步纳入 `artifacts/acceptance/ui-showcase-*/` 目录产物。

## 8. 当前不属于 Demo Mod 拆分问题的剩余项

以下仍是运行时能力边界，不是 demo mod 拆分缺陷：

- 复杂字形 shaping / 浏览器级 BiDi
- 浏览器完整 table layout
- 浏览器完整 form validation / 输入法语义
- Web Overlay 与 Skia 的高保真视觉完全一致

## 9. 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立 Demo Mod 计划 | 明确三种写法拆分、Hub 聚合、皮肤拆分原则 |
| 2026-03-08 | 回填 appearance / image / transition / scroll | 三种写法均补齐可见能力块与截图 |
| 2026-03-08 | 回填 focused capture 策略 | 对首屏下方能力块使用聚焦裁剪，确保截图可见可验收 |
| 2026-03-08 | 回填 Phase 1 selector / stacking showcase | 三种官方写法已补 selector / stack focused crop，markup CSS parity 同步修正 |
| 2026-03-08 | 冻结 Showcase 后续对齐路线 | Phase 1~7 明确要求三种官方写法同步扩展，不允许单写法漂移 |
| 2026-03-08 | 资源化 Markup 样板资产 | HTML/CSS 从 C# 字符串迁移到 `Assets/Showcase/`，三种模式截图证据保持不变 |
| 2026-03-08 | 回填 Phase 3 text lab | 三种官方写法统一补齐共享文本实验块，并补 Phase 3 focused crop 证据 |
| 2026-03-08 | 回填 Phase 4 image / SVG / Canvas lab | 三种官方写法统一补齐共享图片实验块，Markup 走外置 HTML/CSS + C# CodeBehind，截图证据同步进入 acceptance 目录 |
| 2026-03-08 | 收口 Phase 5 animation lab | 三种官方写法已补齐 Phase 5 tri-frame 证据与 acceptance 链路；`animation-*` longhand 明确不作为 Showcase 官方写法 |
