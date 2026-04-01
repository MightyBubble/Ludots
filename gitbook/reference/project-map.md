# 项目地图

本页提供仓库结构的正式导览。

## 1 主要目录

- `src/Core/`：引擎核心
- `src/Apps/`：产品与 adapter 入口
- `src/Tools/`：launcher、bridge 等工具
- `src/Tests/`：测试项目
- `mods/`：内置和示例 Mod
- `assets/`：资源与基础配置
- `gitbook/`：正式文档源
- `docs/`：深度材料、ADR、审计、RFC
- `skills/`：共享 agent skill 源
- `scripts/`：运行、校验和同步脚本

## 2 当前内置 Mod 版图

仓库当前包含 demo、showcase、benchmark、tooling、能力包和 UI 皮肤等多类 Mod，例如：

- `LudotsCoreMod`
- `CoreInputMod`
- `DiagnosticsOverlayMod`
- `GmConsoleMod`
- `FeatureHubMod`
- `MobaDemoMod`
- `RtsDemoMod`
- `ArpgDemoMod`
- `Navigation2DPlaygroundMod`
- `GenreInfoShowcaseMod`
- `ItemSystemShowcaseMod`
- `NarrativeShowcaseMod`
- `RelationshipShowcaseMod`
- `RoadNetworkShowcaseMod`
- `NarrativeFrontendMod`
- `UiSkinClassicMod`
- `UiSkinPaperMod`
- `UiSkinSciFiHudMod`

## 3 文档关系

- 对外发布、开发入口和规范判断以 `gitbook/` 为准
- 仓库深度设计和证据材料位于 `docs/`
- 共享 skill 的正式源位于 `skills/`
