# 面板案例库

面板一案一文：玩家能看见什么、作者怎么写、怎么进场验收。合同见 [面板目录设计](../panel-catalog-designs.md)、[面板视图投影](../panel-view-projection.md)、[查询图集合输出](../query-graph-collection-outputs.md)。

门户一级 tab「面板矩阵」读本目录生成侧栏（与 Graph 节点画廊、Raylib 引擎画廊同级）。

## 类型化集合袋（G12）

> 一袋一面板。查询图写出类型化集合；面板只消费。禁止大合集墙。

- [实体列表](panel-entity-list.md) — 图管圈人/排序，元素模板声明 subject
- [效果条](panel-effect-list.md) — 身上生效的 buff，带着剩余时间
- [效果图鉴](panel-effect-templates.md) — 效果模板袋，只有说明书没有剩余时间
- [编队档案](panel-roster-nested.md) — 单位详情里嵌技能格
- [身上的印记](panel-present-tags.md) — 标签袋
- [背包堆叠](panel-inventory-aggregate.md) — 物品实例袋 + aggregate 展示
- [物品图鉴](panel-item-definitions.md) — 物品定义袋
- [进行中的差事](panel-active-tasks.md) — 任务实例袋
- [进行中的活动](panel-active-activities.md) — 活动实例袋
- [谁会火球](panel-ability-holders.md) — 技能反查持有者（source=input）
- [修行进度](panel-progression-nodes.md) — 进度节点袋
- [开箱布局教室](panel-author-layout-kit.md) — 同一芯片 × 竖列/网格/横栏 + image 图标

## 前四案（全设计）

- [玩家信息聚合 — 纯展示 · global scope · 活状态中转](panel-player-aggregate.md) — `panel.player.aggregate`
- [时间控制 — 交互全链 · 事件/意图/回读闭环](panel-time-control.md) — `panel.time.control`
- [设置 — 模态浮层 · 连续手势 · 全局副作用](panel-settings.md) — `panel.settings`
- [全局指令 — 零变量纯命令 · G6 缺口样板](panel-command-global.md) — `panel.command.global`

## 其余案例设计

- [时间流逝（纯展示走表）](panel-time-elapsed.md) — `panel.time.elapsed`
- [日期（纯展示）](panel-date-cycle.md) — `panel.date.cycle`
- [全局功能 tab（交互路由）](panel-tabs-global.md) — `panel.tabs.global`
- [全局信息横幅（纯展示）](panel-info-banner.md) — `panel.info.banner`
- [子系统入口（交互路由）](panel-subsystem-entries.md) — `panel.subsystem.entries`
- [小地图（纯展示覆盖层）](panel-minimap.md) — `panel.minimap`
- [关系图指示（纯展示）](panel-relation-indicator.md) — `panel.relation.indicator`
- [选中标记（纯展示）](panel-selection-marker.md) — `panel.selection.marker`
- [屏外指示（纯展示）](panel-offscreen-indicator.md) — `panel.offscreen.indicator`
- [场景染色（全屏效果非框）](panel-scene-tint.md) — `panel.scene.tint`
- [区域指示（纯展示）](panel-region-indicator.md) — `panel.region.indicator`
- [路网指示（纯展示）](panel-road-indicator.md) — `panel.road.indicator`
- [实体信息聚合（纯展示）](panel-entity-aggregate.md) — `panel.entity.aggregate`
- [实体关系（纯展示）](panel-entity-relation.md) — `panel.entity.relation`
- [集合聚合（纯展示）](panel-collection-aggregate.md) — `panel.collection.aggregate`
- [选中路由（机制，不产配置）](panel-context-route.md) — `panel.context.route`
- [关联实体集（纯展示）](panel-linked-entities.md) — `panel.linked.entities`
- [事件面板（纯展示）](panel-events-feed.md) — `panel.events.feed`
- [日志入口（交互）](panel-events-entry.md) — `panel.events.entry`
- [事件日志（交互模态）](panel-events-log.md) — `panel.events.log`
- [编队信息（纯展示）](panel-formation-info.md) — `panel.formation.info`
- [任务面板（交互）](panel-quests.md) — `panel.quests`
- [进度节点树（交互模态）](panel-progress-tree.md) — `panel.progress.tree`
- [生产队列（#1012 验收场景）](panel-production-queue.md) — `panel.production.queue`
- [状态条（纯展示）](panel-unit-status.md) — `panel.unit.status`
- [技能指令（#1015 主战场）](panel-abilities.md) — `panel.abilities`
- [物品装备（交互）](panel-loadout.md) — `panel.loadout`
- [视图过滤器（交互）](panel-view-filter.md) — `panel.view.filter`
- [额外文本（纯展示）](panel-extra-text.md) — `panel.extra.text`
- [图鉴背包（交互模态）](panel-collection-book.md) — `panel.collection.book`
