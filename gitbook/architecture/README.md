# 架构

本章节给出 Ludots 当前正式架构的入口说明。更长的设计推导、实现细节和演进证据位于 `docs/architecture/`，但正式总览以本章节为准。

## 核心主题

- [运行时总览](runtime-overview.md)
- [Mod 架构](mod-architecture.md)
- [GAS 分层架构](gas-layered-architecture.md)

## 当前主线重点

- launcher 已进入 graph-backed SSOT 阶段，运行时由 launcher graph artifact 驱动
- Core 现已包含 `TimeFlow`、`Items`、`Narrative`、`Relationships` 等正式运行时能力
- 输入、选择、实体信息面板、路网移动与 narrative frontend 都已有主线实现和 showcase 入口
- `docs/architecture/` 中的长篇页面覆盖了这些能力的深度说明，GitBook 这里负责给出正式导航和判断口径

## 核心原则

- Core 与平台解耦
- System 必须归属明确 phase
- Mod 是功能接入和组合的主要单位
- 跨层写入优先通过正式 Sink 和 Pipeline

## 深度材料

- 仓库架构索引：`docs/architecture/README.md`
