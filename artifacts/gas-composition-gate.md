# GAS Composition Gate — Self Review

- **Task / Issue**: 共享选中链补 `QueryFilterSelectable` 门——切3（1462574fc3）删除 C# CommandSourceAcquisitionSystem 后，`CommandSourceSelectableTag ∧ CommandSourceSelectableState.Enabled` 的可选性合同在图链（graph.core.select_begin/hit/commit）上无人执行，框选把 `IsEnabled=false` 的演示实体选进来（InteractionShowcase_SingleClickReselect×2 红）。
- **Date**: 2026-09-22
- **Agent / Author**: ZCode（test-red-clearing session）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 查询过滤节点 + graph.core.select_hit 连线）

结论: **PASS**

一句话理由: 交付物是单一职责的 TargetList 原位过滤 op（镜像同链 QueryFilterKnowledgeVisible(485) 先例）加一条图连线，无新 enum/开关/profile DSL。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 候选原位过滤 op | 0 | GraphNodeOp.QueryFilterSelectable(487) + handler + descriptor + 编译器 case |
| 可选性判定复用 | 0（既有） | CommandSourceEligibility.IsSelectableNow（不动） |
| Api 面 | 0 | IGraphRuntimeApi.FilterCommandSourceSelectable + GasGraphRuntimeApi 实现 |
| 选中链接线 | 2 | mods/LudotsCoreMod graph.core.select_hit.json 加过滤节点 |
| 画廊/覆盖工件 | 3 | vignette + 生成器 upsert（registry/maps/entry mods/launcher/wiki） |

## 3. Reuse list

- Handlers: GasGraphOpHandlerTable 既有注册面；HandleQueryFilterKnowledgeVisible 处理器模式
- Queues / Systems: 无新系统
- Resolvers / Registries: CommandSourceEligibility.IsSelectableNow（唯一判定入口，不重写）；GraphOpDescriptorTable；graph_node_op_coverage.registry.json
- Existing presets / graphs: graph.core.select_begin/hit/commit 既有链

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| QueryFilterSelectable(487) | TargetList 原位剔除非（CommandSourceSelectableTag ∧ State.Enabled）候选，保序 | 现有过滤 op（Team/Template/AttributeRange/TagAny/TagNone/KnowledgeVisible/NotEntity/Layer）无一读 CommandSourceSelectableState；用 Template 过滤是数据 hack 且 fork 合同 |

## 5. Transaction boundary

必须原子 rollback 的步骤: 无（纯查询过滤，无副作用，无写面）

## 6. Config SSOT

行为配置落在: graph JSON（mods/LudotsCoreMod/assets/GAS/graphs/graph.core.select_hit.json + 画廊 vignette）

是否新增 JSON schema: **NO**
