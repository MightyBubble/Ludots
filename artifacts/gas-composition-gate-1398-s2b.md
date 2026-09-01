# GAS Composition Gate — #1398 S2b（新机制批）

## 任务摘要

RFC #1398 v3 §2 引擎缺口 1/2/3/4/9 的 S2b 切片：

1. `InteractionContextProfileDefinition` 增 `bindings[]` / `triggers[]`（加载链 fail-fast）
2. context 门控接线：激活 context 集合 diff → 挂/卸 profile triggers[] 引用的 TriggerGraph 监听
3. 衍生子 context（parent/并存集合/scope 生命周期）+ 新 graph op `ActivateContext`/`DeactivateContext`
4. presenter 事件 kind `ContextActivated`/`ContextDeactivated`
5. `EntityTemplate.initialInteractionContext`（出生挂 Instance）+ 下游「按 eventKey 收 → 写集合」通用 handler

## GAS Composition Gate — Self Review

- **Task / Issue**: #1398（S2b，Case E 最后一批引擎增量）
- **Date**: 2026-09-01
- **Agent / Author**: pi（codex/case-e-s2b-mechanisms worktree）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 节点 `ActivateContext`/`DeactivateContext` + 已有 op 的数据引用扩展；全部行为语义留在图/配置数据里）

结论: **PASS**

一句话理由: 衍生 context 的激活/停用是 graph 可组合的两个 Layer 0 世界侧效 op（单一职责：改实体身上的 context 状态 + scope 生命周期），无新 enum、无 preset 开关；modifier→op 映射（replace/add/subtract）继续留在图内计算（裁决 F 既有口径）。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| ActivateContext / DeactivateContext | 0 | GraphNodeOp 枚举 + handler + GasGraphRuntimeApi 内核（InteractionContextInstanceRuntime） |
| context 实例状态 | 0 | 组件改名 `InteractionContextInstance` + 并存集合 `InteractionContextInstances`（纯数据，op 写入；宪法命名令落地） |
| context 激活/结束事件 | 0 | PresentationEventKind 两枚举值 + 发布接线 |
| triggers[] 挂/卸 | 1 | 新 diff 系统（InteractionContextTriggerGateSystem），复用 TriggerGraphMounting/TriggerManager |
| 出生挂 Instance | 1 | RuntimeEntitySpawnSystem / MapLoader spawn 钩子（复用 AbilityExecInteractionContextSystem 同款 TryCreateActiveContext） |
| Case E 具体行为（框选等） | 2（S3，不在本片） | 纯配置资产 |
| presenter scope 清理 | 0 | 复用 PresenterCommand(DestroyPresenterScope) 正式绑定点 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 既有注册机制；`TriggerGraphEntryFiltersEvaluator`
- Queues / Systems: `PresenterCommandBuffer`、`PresentationEventStream`、`RuntimeEntitySpawnSystem`、`EntityTriggerGraphMounts`（先例）、`AbilityExecInteractionContextSystem`（diff 先例）
- Resolvers / Registries: `InteractionContextProfileRegistry`（扩展 Install）、`GraphProgramRegistry`、`GraphIdRegistry`、`ConfigKeyRegistry`（op 符号）、`PresenterScopeTagRegistry`、`EntityCollectionStore.KeyRegistry`、`StringIntRegistry`
- Existing presets / graphs: TriggerGraph 挂载编译链（`TriggerGraphMounting.AppendEntryTriggers` 的姊妹路径）、`ContextBoundCollectionWriter`（内核复用 EntityCollectionStore/DomainRoutedCollectionWriter）

### 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `ActivateContext` (474) | 在实体上激活一个衍生交互 context（校验 parent、幂等点名、建 scope、发激活事件） | 现有 op 463 SetInteractionMode 只切 InteractionMode（输入侧模式），无 scope、无 parent、无并存集合；pushFrame 是 cast 链专用的 InteractionOp 不在 graph VM 里 |
| `DeactivateContext` (475) | 停用一个衍生 context（自动清子、DestroyScope 整组清、发结束事件） | 同上；现有无任何 op 能移除衍生 context / 销毁 scope |

（其余交付物无新 Layer 0 op）

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（op 是单步实体状态写入 + 事件/命令入队；失败路径 fail-fast 点名，不做部分回滚。scope 销毁走 PresenterCommandBuffer 在同帧被 PresenterRuntimeSystem 消费）。

### 6. Config SSOT

行为配置落在: `Input/interaction_context_profiles.json`（bindings/triggers 字段，扩展既有 schema 非新文件）、graph JSON（衍生激活/停用/集合语义全在图体）、`templates.json`（initialInteractionContext 字段）。

是否新增 JSON schema: **NO**（只扩既有 profile/template schema 字段；下游 handler 是引擎内核服务，消费 graph 透传事件，不新增 profile DSL）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（出生挂 Instance 走 RuntimeEntitySpawnSystem/MapLoader 既有 spawn 钩子）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（op 失败一律点名；无静默回退）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**（换一种框选命中算法 = 换 query graph；换修饰键语义 = 改图内 op 计算；新增一种衍生 context = 新 profile 行 + 新 trigger graph，零 Core 改动）。

（未选 Core enum → PASS）

## 复用 / 新增清单（与 ai-assisted-development §4.2 合并）

| 类型 | 项 |
|------|-----|
| 复用 | TriggerManager、TriggerGraphMountTrigger、PresenterCommand/PresenterCommandBuffer、PresenterEntityRuntime.Create/DestroyScope、EntityCollectionStore、DomainRoutedCollectionWriter、InteractionContextProfileRegistry、RuntimeEntitySpawnSystem 既有钩子位、PresentationEventStream、ConfigKeyRegistry 符号链 |
| 新增 Layer 0 op | ActivateContext(474)、DeactivateContext(475) |
| 新增 Layer 1 | InteractionContextTriggerGateSystem（激活集合 diff → 挂/卸）、DerivedInteractionContextRuntime（op 内核 + 事件发布 + scope 命令）、EventKeyedCollectionWriter（按 eventKey 收 → 写集合） |
| 新增 Layer 2 | 无（S3 纯数据） |
| 禁止 | 无新 profile DSL、无平行加载器、无 enum 开关——均未触犯 |
