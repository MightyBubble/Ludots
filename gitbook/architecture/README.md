# 架构

本章节给出 Ludots 当前正式架构的入口说明。更长的设计推导、实现细节和演进证据位于 `docs/architecture/`，但正式总览以本章节为准。

## 核心主题

- [运行时总览](runtime-overview.md)
- [Mod 架构](mod-architecture.md)
- [GAS 分层架构](gas-layered-architecture.md)
- [Exchange Operations](exchange-operations.md)
- [实体仿真分层与车道](entity-simulation-layering.md)
- [实体仿真工作流拆分](entity-simulation-workstreams.md)
- [实体仿真阶段验收](entity-simulation-uat.md)
- [能力标准 Showcase](capability-standard-showcases.md)
- [UAT 可玩 Showcase 矩阵](uat-playable-showcase-matrix.md)
- [Prefab Grounding 与 Visual Height](prefab-grounding-and-visual-height.md)
- [Core Minimap Authoring](core-minimap-authoring.md)
- [Map-Owned Participant Contract](map-owned-participant-contract.md)
- [Performer-as-Actor 架构总览](performer-as-actor-architecture.md)
- [Instanced Batch 外部 Source Contract](instanced-batch-source-contract.md)
- [Map Batch Performer Param Overrides](map-batch-performer-param-overrides.md)
- [Retained Static Incremental Projection](retained-static-incremental-projection.md)
- [Performer 参数黑板与 Animator 统一](performer-param-blackboard.md)
- [Performer Transform、Grounding 与 Attachment](performer-transform-and-attachment.md)
- [Performer Raylib UAT 测试计划](performer-raylib-uat.md)
- [Performer 现有基建收尾整合](performer-legacy-consolidation.md)
- [Performer 开发看板](performer-development-kanban.md)
- [Performer 编译式执行分层](performer-compiled-lanes.md)

## 当前主线重点

- launcher 已进入 graph-backed SSOT 阶段，运行时由 launcher graph artifact 驱动
- Core 现已包含 `TimeFlow`、`Items`、`Exchange`、`Narrative`、`Relationships` 等正式运行时能力
- 输入、选择、实体信息面板、路网移动与 narrative frontend 都已有主线实现和 showcase 入口
- 大规模实体场景的下一阶段主线，是把 `Authority` 与 `Budgeted` 仿真车道、碰撞层过滤、AOI/LOD 调度和 mass crowd 展示收敛成同一套正式组件规范
- Raylib 侧已补充一个“脱离 performer/entity 行为”的直接 ISM benchmark，用于隔离最终绘制瓶颈；当前证据表明 30K 黑铁匠铺 mesh 的平台层 instanced draw 已能稳定跑通，优先暴露出的风险点在 Skia final overlay，而不是平台层 mesh draw
- Retained presentation 的 content revision 与 adapter target/projection generation 是两条独立真相：content revision 只表示表现内容变化；adapter target unavailable -> ready 或 target 替换必须通过 Core-owned generation 触发 retained projection replay
- 商业引擎 adapter（例如开发者仓库中的 UE5 adapter）的 host-bound map session 只能由 focused map SSOT 与显式 host binding 推导，禁止用菜单态、world 名、tag 或 view mode 充当 ownership 真相
- prefab grounding、visual height 与 adapter parity 必须共用同一套 Core-owned contract，禁止把 grounding 语义下放给 adapter 或 showcase 私有 glue
- `docs/architecture/` 中的长篇页面覆盖了这些能力的深度说明，GitBook 这里负责给出正式导航和判断口径

## 核心原则

- Core 与平台解耦
- System 必须归属明确 phase
- Mod 是功能接入和组合的主要单位
- 跨层写入优先通过正式 Sink 和 Pipeline

## 深度材料

- 仓库架构索引：`docs/architecture/README.md`
