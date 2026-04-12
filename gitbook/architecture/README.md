# 架构

这里维护 Ludots 当前正式架构入口。

## 核心主题

- [运行时总览](runtime-overview.md)
- [Mod 架构](mod-architecture.md)
- [GAS 分层架构](gas-layered-architecture.md)
- [Navigation2D Authoring](navigation2d-authoring.md)
- [Navigation2D Group Commands](navigation2d-group-commands.md)
- [Navigation2D Crowd Relationships](navigation2d-crowd-relationships.md)
- [Navigation2D Knockback Override](navigation2d-knockback-override.md)
- [Navigation2D UAT](navigation2d-uat.md)
- [视觉地形分层](visual-terrain-layering.md)
- [视觉地形开放世界方案](visual-terrain-open-world.md)
- [视觉地形编辑原型](visual-terrain-prototype.md)
- [视觉地形编辑器 UAT 与 MVP](visual-terrain-editor-uat.md)

## 当前主线重点

- launcher 已进入 graph-backed SSOT 阶段，运行时由 launcher graph artifact 驱动
- Core 已包含 `TimeFlow`、`Items`、`Narrative`、`Relationships` 等正式运行时能力
- 输入、选择、实体信息面板、路网移动、narrative frontend 都已有主线实现和 showcase 入口
- visual terrain 当前已明确分成逻辑 provider、visual terrain data、`IVisualHeightmap` 三层

## 核心原则

- Core 与平台解耦
- System 必须归属明确 phase
- Mod 是功能接入与组合的主要单位
- provider 与数据层分离，禁止把某种逻辑地图结构硬编码成视觉地形真相

## 深度材料

- 仓库架构索引：`docs/architecture/README.md`
