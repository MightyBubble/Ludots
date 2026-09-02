# 架构

本章节给出 Ludots 当前正式架构的入口说明。更长的设计推导、实现细节和演进证据位于 `docs/architecture/`，但正式总览以本章节为准。

## 核心主题

- [运行时总览](runtime-overview.md)
- [服务器权威联机运行时](authoritative-multiplayer-runtime.md)
- [UI 渲染控制与 Surface 所有权](ui-rendering-and-surface-ownership.md)
- [面板目录设计：配置形状与线框](panel-catalog-designs.md) — 总合同（graph-pinned panels）
- [Mod 架构](mod-architecture.md)
- [Mod Extensible Runtime](mod-extensible-runtime.md)
- [Mod Extensible Runtime Showcases](mod-extensible-runtime-showcases/README.md)
- [GAS 分层架构](gas-layered-architecture.md)
- [属性写入权威](attribute-write-authority.md) — **current 直写存活；聚合器只算有效上限；裸写由 IL 守卫收口**
- [图分层：Flow / Script 与行为调度](graph-layering-flow-and-behavior.md)
- [图能力唯一入口](graph-capability-status.md) — **进度、还开着的活、不该合的 PR，只认这里**
- [统一时间轴编辑器](unified-timeline-editor.md) — 演出序列 / 技能 exec / Presenter 计时共用轨道界面，只换上下文适配器
- [图复用库合同：FuncLib / ActionLib](graph-funclib-actionlib-contract.md) — **纯函数库 vs 可挂起动作库；Effect 时间轴与阶段表达力补丁**
- [（已废止）TagDisplay 专线查表](tag-display-lookup.md)
- [通用图查表（ResolveTableRow + TableRead）](graph-table-lookup.md) — **查表 SSOT**
- [GAS、订单与输入运行时合同](gas-order-input-runtime-contract.md)
- [实时技能工作台（LSW）架构契约](live-skill-workbench.md) — **热调试技能数值 / 属性 / 效果链 / AI 草稿与热应用分级的 SSOT**
- [Input Order Routing 与 Spawn Target 基建](input-order-and-spawn-target.md)
- [Entity Lifecycle 原子 Op](entity-lifecycle-atomic-ops.md) — **实体结构替换 / deploy consume source 的 SSOT**
- [时间体系](time-system.md)
- [Exchange Operations](exchange-operations.md)
- [Quest Core Infrastructure](../../docs/architecture/quest_core_infra.md)
- [通用存档系统](save-system.md)
- [AI Utility Autocast 契约](ai-utility-autocast-contract.md)
- [实体仿真分层与车道](entity-simulation-layering.md)
- [实体仿真工作流拆分](entity-simulation-workstreams.md)
- [实体仿真阶段验收](entity-simulation-uat.md)
- [能力标准 Showcase](capability-standard-showcases.md)
- [UAT 可玩 Showcase 矩阵](uat-playable-showcase-matrix.md)
- [Prefab Grounding 与 Visual Height](prefab-grounding-and-visual-height.md)
- [Structure Collision Surfaces](structure-collision-surfaces.md)
- [Core Minimap Authoring](core-minimap-authoring.md)
- [Core Field2D](core-field2d.md)
- [Global Field Rendering](global-field-rendering.md)
- [Raylib Render Productization](raylib-render-productization.md)
- [Quarks Particle Schema](quarks-particle-schema.md)
- [Map-Owned Participant Contract](map-owned-participant-contract.md)
- [Transport Network SSOT](transport-network-ssot.md)
- [Placement Validation SSOT](placement-validation-ssot.md)
- [空间尺度与分辨率 SSOT](spatial-scale-and-resolution-ssot.md)
- [MassNavigation 数值域与确定性边界](mass-navigation-numeric-domain.md)
- [Logic Terrain and Topology](../reference/logic-terrain-and-topology.md)
- [NavBakeContext 与统一烘焙服务](../reference/nav-bake-context.md)
- [Presenter-as-Actor 架构总览](presenter-as-actor-architecture.md)
- [Instanced Batch 外部 Source Contract](instanced-batch-source-contract.md)
- [Map Batch Presenter Param Overrides](map-batch-presenter-param-overrides.md)
- [Retained Static Incremental Projection](retained-static-incremental-projection.md)
- [Presenter 参数黑板与 Animator 统一](presenter-param-blackboard.md)
- [Presenter Transform、Grounding 与 Attachment](presenter-transform-and-attachment.md)
- [Presenter Raylib UAT 测试计划](presenter-raylib-uat.md)
- [Presenter 现有基建收尾整合](presenter-legacy-consolidation.md)
- [Presenter 开发看板](presenter-development-kanban.md)
- [Presenter 编译式执行分层](presenter-compiled-lanes.md)
- [Browser Runtime Provider Adapter Guide](browser-runtime-provider-adapter-guide.md)
- Browser UI Runtime：正式 contract 位于 `docs/architecture/browser_ui_runtime.md`，用于把真实 Web App 作为平台无关 browser surface 嵌入 Ludots UI；它不改变 native Markup 无 JS 的边界
- WebUI DataPlane：正式边界位于 `docs/architecture/webui_dataplane_architecture.md`，归属 `Ludots.WebUI` 高层，复用 `EntityCollectionStore` 与 Minimap marker buffer 的 SoA / bucket / drop diagnostics 模式；UE5 BLUI 只作为外部 transport adapter
- WebUI Panel Kit Manifest（WPK-1）：面板组合合同位于 `docs/architecture/webui_panel_kit_manifest.md`；复用 `UiSurfaceHost` 与 DataPlane topic，不新建平行 host；加载期校验 topic/profile/layout/surface 引用
- WebUI Notification Panel（WPK-7）：独立消息 SSOT 位于 `docs/architecture/webui_notification_panel.md`；不依赖 NarrativeFrontend / Quest / showcase toast 私有状态；文案走 WPK-5 token 校验；Web 只渲染 DataPlane snapshot
- WebUI TechTree / Progression Panel（WPK-9）：Progression 节点面板合同位于 `docs/architecture/webui_techtree_progression_panel.md`；状态来自 Progression runtime/requirement，不新建 TechTreeStore
- WebUI Panel Kit Showcase Family（WPK-10）：独立面板 showcase 位于 `docs/architecture/webui_panel_kit_showcase_family.md`；一面板类型一 showcase，面向新玩家上手

## 当前主线重点

- Entity Association Core 的计划与 ADR SSOT 是 GitHub issue #239；ADR 正本是 #244（AAC-1）。不要在 `docs/adr/` 为 AAC 新增平行 ADR 文件；AAC-2~AAC-12 必须引用 #244 的存储策略、ScopeKey、组合契约、红线与 2.5 UAT showcase capability mod 标准。需要玩家可见 showcase 的子单是 #245、#246、#247、#248、#249、#250、#251、#253；meta/卫生/护栏例外是 #244、#252、#254、#255。
- launcher 已进入 graph-backed SSOT 阶段，运行时由 launcher graph artifact 驱动
- Core 现已包含 `TimeFlow`、`EntityLocalClock`、`Items`、`Exchange`、`Quest`、`Dialogue`、`Sequencer`、`Relationships` 等正式运行时能力（Story Runtime SSOT：`docs/architecture/story_runtime_dialogue_sequencer.md`）
- 输入、选择、实体信息面板、路网移动与故事 frontend surface 都已有主线实现和 showcase 入口
- 大规模实体场景的下一阶段主线，是把 `Authority` 与 `Budgeted` 仿真车道、碰撞层过滤、AOI/LOD 调度和 mass crowd 展示收敛成同一套正式组件规范
- Raylib 侧已补充一个“脱离 presenter/entity 行为”的直接 ISM benchmark，用于隔离最终绘制瓶颈；当前证据表明 30K 黑铁匠铺 mesh 的平台层 instanced draw 已能稳定跑通，优先暴露出的风险点在 Skia final overlay，而不是平台层 mesh draw
- Retained presentation 的 content revision 与 adapter target/projection generation 是两条独立真相：content revision 只表示表现内容变化；adapter target unavailable -> ready 或 target 替换必须通过 Core-owned generation 触发 retained projection replay
- 商业引擎 adapter（例如开发者仓库中的 UE5 adapter）的 host-bound map session 只能由 focused map SSOT 与显式 host binding 推导，禁止用菜单态、world 名、tag 或 view mode 充当 ownership 真相
- prefab grounding、visual height 与 adapter parity 必须共用同一套 Core-owned contract，禁止把 grounding 语义下放给 adapter 或 showcase 私有 glue
- `docs/architecture/` 中的长篇页面覆盖了这些能力的深度说明，GitBook 这里负责给出正式导航和判断口径

## 核心原则

- Core 与平台解耦
- System 必须归属明确 phase
- Mod 是功能接入和组合的主要单位
- 跨层写入优先通过正式 Sink 和 Pipeline
- 浏览器内核、商业引擎宿主与平台窗口生命周期必须留在 adapter；Core 只定义可复用的 C# contract

## 深度材料

- 仓库架构索引：`docs/architecture/README.md`
