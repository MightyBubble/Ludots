# UI Runtime Demo Mod 计划

Date: 2026-03-07
Status: Implemented baseline, expansion planned
Scope: 官方 UI Showcase Demo Mod 拆分、三种写法展示、皮肤切换展示
Type: UI-only planning artifact

## 1 目标

本计划定义 UI 官方 Demo Mod 的拆分原则，避免把所有演示逻辑塞进一个大而全的演示包中。

目标如下：

- 让 Compose / Reactive / Markup 三种官方写法各自有独立 Showcase Mod
- 让皮肤切换、同一 DOM 换不同皮肤的能力有独立演示 Mod
- 让 Hub Mod 只负责入口聚合，不承载所有页面实现
- 让每类 Showcase 都能产出独立可见验收产物

## 2 当前拆分结果

| Mod | 状态 | 说明 |
|-----|------|------|
| `mods/UiShowcaseCoreMod/` | Implemented | 承载共享页面工厂、共享样式、DOM hash 与皮肤基座 |
| `mods/UiShowcaseHubMod/` | Implemented | 承载 Showcase 入口与导航 |
| `mods/UiComposeShowcaseMod/` | Implemented | Compose 官方写法展示 |
| `mods/UiReactiveShowcaseMod/` | Implemented | Reactive 官方写法展示 |
| `mods/UiMarkupShowcaseMod/` | Implemented | Markup + C# CodeBehind 官方写法展示 |
| `mods/UiSkinShowcaseMod/` | Implemented | 同一 DOM / Scene 换不同皮肤 |
| `mods/UiDomSkinFixtureMod/` | Implemented | 皮肤夹具与共享 DOM |
| `mods/UiSkinClassicMod/` | Implemented | Classic 皮肤 |
| `mods/UiSkinSciFiHudMod/` | Implemented | SciFi HUD 皮肤 |
| `mods/UiSkinPaperMod/` | Implemented | Paper 皮肤 |

## 3 当前官方页面范围

当前 Showcase 基线已覆盖：

- `OverviewPage`
- `ControlsPage`
- `FormsPage`
- `CollectionsPage`
- `OverlaysPage`
- `StylesPage`
- Markup 专属 `PrototypeImportPage`

## 4 已批准扩展页

后续在保持 Mod 拆分原则不变的前提下，继续补充以下官方 Showcase 页面：

- `AppearancePage`：阴影、渐变、描边、模糊、毛玻璃
- `TypographyPage`：换行、字体、自定义字体、多语言、RTL/BiDi
- `AnimationPage`：Tween 过渡、状态变化、入场/退场、主题过渡
- `TablePage`：表头、单元格、行列布局、排序/选中态展示
- `AdvancedFormsPage`：Radio、校验状态、焦点链、表单组合控件
- `ImagePage`：九宫格、裁剪、拉伸、皮肤图集

## 5 模块边界规则

- `UiShowcaseCoreMod` 只放共享工厂、共享样式、共享 fixture，不放具体入口耦合逻辑
- `UiShowcaseHubMod` 只做导航与入口聚合
- 三种官方写法 Mod 各自维护自己的示例入口与页面装配
- 皮肤展示通过同一 DOM / Scene 挂不同 `UiThemePack` 或等价皮肤包完成

## 6 变更历史

| 日期 | 变更 | 说明 |
|------|------|------|
| 2026-03-07 | 建立 Demo Mod 计划 | 明确三种写法拆分、Hub 聚合、皮肤拆分原则 |
| 2026-03-07 | 编码修复 | 将历史乱码计划文档改写为 UTF-8 可读文档 |
| 2026-03-07 | 范围扩展回填 | 新增外观、字体、动画、表格、表单、图片页面规划 |
