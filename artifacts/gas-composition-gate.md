## GAS Composition Gate — Self Review

- **Task / Issue**: Graph editor and live TriggerGraph debug stream (issue #1030, item 7 follow-up)
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 本次交付是对现有 GraphControlFlowDocument、Graph op 描述表和 TriggerGraph 执行的编辑/观测组合，不新增 profile enum、preset 开关或第二套 VM。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Graph 作者数据读取、编译诊断、保存 | 2 | 现有 `GraphProgramAuthoringFrontDoor` 与 `Ludots.Editor.Bridge` |
| 节点布局 sidecar | 2 | 编辑器工具侧独立 sidecar JSON |
| TriggerGraph 节点/寄存器变化 trace | 0/1 观测旁路 | 固定容量 `GraphDebugTrace`，不参与 gameplay 语义 |
| AgentBridge 增量 drain | 2 | 现有 `AgentToolRegistry` 与游戏线程 pump |

### 3. Reuse list

- Handlers: 现有 `GasGraphOpHandlerTable`，不新增 op handler。
- Queues / Systems: 现有 AgentBridge game-thread pump、TriggerGraph slice/resume 管线。
- Resolvers / Registries: `GraphProgramRegistry` source map、`MapSession.Triggers`、`GraphOpDescriptorTable`。
- Existing presets / graphs: `GraphControlFlowDocument`、真实 `graphs.json`。

### 4. New Layer 0 ops (if any)

N/A — trace 不是执行 op，不改变 Graph program。

### 5. Transaction boundary

无 gameplay 事务变化；trace 记录失败时只报告 ring overflow/dropped count，不影响执行结果。

### 6. Config SSOT

行为配置落在: 现有 graph JSON；编辑器布局落在独立 sidecar。是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**。

## Issue #1177 Beat 5 — Field-region ability scope — 2026-08-26

- **Task / Issue**: Add the `field_jing_yang_transit` region-scoped ability demonstration.
- **Date**: 2026-08-26
- **Agent / Author**: GPT-5.6 Sol

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 区域过境复用现有 TriggerGraph 事件、effect tag 和 ability validation graph 组合，不新增 Graph op、profile enum、preset 开关或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 区域进入/退出授予与移除 scope tag | 2 | 现有 `FieldRegionEntered` / `FieldRegionExited` TriggerGraph + `ApplyEffectTemplate` / `RemoveEffectTemplate` |
| Ability 激活门 | 2 | 现有 Validation graph + `HasTag` + `activationPrecondition` |
| 成功施放地图状态 | 2 | 现有 Effect graph + `ReadMapVarInt` / `AddInt` / `WriteMapVarInt` |
| 当前区域只读查询 | Core query | `RegionMembershipCm` + `FieldSessionStore` + `RegionIdRegistry` |

### 3. Reuse list

- Handlers: `HasTag`, `ApplyEffectTemplate`, `RemoveEffectTemplate`, `ReadMapVarInt`, `AddInt`, `WriteMapVarInt`.
- Queues / Systems: `FieldRegionMembershipSystem`, existing TriggerGraph dispatch, GAS effect request/processing pipeline.
- Resolvers / Registries: `FieldSessionStore`, `RegionIdRegistry`, `TagRegistry`, `EffectTemplateIdRegistry`, `AbilityDefinitionRegistry`, `GraphProgramRegistry`.
- Existing presets / graphs: `Buff` infinite effect, Validation graph authoring, `AbilityExecLoader.activationPrecondition`.

### 4. New Layer 0 ops (if any)

N/A — no new Graph op, effect handler, or lifecycle operation.

### 5. Transaction boundary

No new transaction boundary. Region transitions publish existing ordered exit/enter events; each graph applies one existing effect operation.

### 6. Config SSOT

Behavior config remains in the showcase's existing `GAS/graphs.json`, plus standard `GAS/effects.json` and `GAS/abilities.json` catalog assets. 是否新增 JSON schema: **NO**。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**。

## Issues #714-#719 AI/GAS Order Boundary — Pre-Implementation Gate — 2026-07-31

- **Task / Issue**: Implement issues #714-#719 after PR #713, keeping ability lockout as duration Effect data, keeping Utility AI out of GAS ability eligibility, and converging AI output on typed Order contracts and read-only scoring.
- **Date**: 2026-07-31
- **Agent / Author**: Codex
- **Baseline**: `origin/main` cached at `74513182ab420dc950844d26882000ec54e030a7` (`Merge pull request #713 from MightyBubble/codex/gas-graph-effect-ssot`). Network fetch retried but GitHub reset the connection; the cached remote head already includes the confirmed merged PR #713.
- **Status**: PRE-IMPLEMENTATION PASS.

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A.

结论: PASS.

一句话理由: Temporary ability lockout is authored as duration Effects that grant tags; abilities read `blockTags`, AI submits typed Orders, and scoring stays read-only.

## GAS Composition Gate - TriggerGraph Domain Expansion - 2026-08-26

- **Task / Issue**: Entity TriggerGraph attachment scope, ability/mod domains, global map route, and fixed-step Mod continuation.
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 本次交付复用现有 TriggerManager、Graph VM、事件桥和 attachment 关系，只增加挂载域/路由数据与固定步恢复脉冲，不新增 profile DSL、生命周期 op、平行物化管线或第二事件总线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Entity attachment scope matching | 2 | `TriggerGraphMountTrigger` + existing `ChildOf`/aggregate marker |
| Ability/Mod graph mount filters | 2 | existing `TriggerManager` + `AbilityId`/`ModId` payload contracts |
| Mod suspended-run continuation | 1/2 | `ModTriggerResumeClockSystem` in `DeferredTriggerCollection` + existing VM cursor |

### 3. Reuse list

- Handlers: existing `GasGraphOpHandlerTable` and `GasGraphRuntimeApi`; no new graph op.
- Queues / Systems: existing `TriggerManager`, `DeferredTriggerCollection`, `MapHeartbeatClockSystem`, and map lifecycle registration.
- Resolvers / Registries: existing `GraphProgramRegistry`, `AbilityDefinitionRegistry`, `CustomEventNameRegistry`, and `GraphIdRegistry`.
- Existing presets / graphs: existing TriggerGraph assets and attachment/spawn pipeline.

### 4. New Layer 0 ops

N/A - no atomic gameplay op or effect handler was added.

### 5. Transaction boundary

No new gameplay transaction; mount registration is load-time fail-closed and Mod unload removes all owned triggers before lifecycle teardown.

### 6. Config SSOT

Behavior remains in existing map/entity/ability/mod TriggerGraph declarations and event catalogs. No new JSON schema.

### 7. Red flag scan

- [x] No profile inherit/placement enum.
- [x] No parallel spawn/morph pipeline.
- [x] No placement validation in lifecycle ops.
- [x] No silent fallback; unknown, cross-domain, and internal continuation events fail closed.

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**。

---

## GAS Composition Gate — Self Review (#1108)

- **Task / Issue**: #1108 放置实体自动变量化（一期 = LoadPlacedEntity Entity 类全链）
- **Date**: 2026-08-24
- **Agent / Author**: ZCode (graph-editor-audit worktree)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** —— 新 graph 节点 `LoadPlacedEntity`（opcode 416，TriggerGraphOnly，SymbolImm）+ 只读查表运行时通道。

结论: **PASS**

一句话理由: 交付物是新 graph op 与已有 `MapLoadEntityIndex` 的只读复用；不新增 profile enum、preset 开关或平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| LoadPlacedEntity op（E[Dst] := 放置实体） | 0 | `GraphNodeOp` 416 + `GasGraphOpHandlerTable` handler（Pure 归类） |
| 挂载期 InstanceId fail-closed 校验 | 3（装载层） | `TriggerGraphMounting.AppendEntryTriggers`（复用 filters.instanceId 同款先例与 entityIndex 入参） |
| 运行时查表通道 | 0 服务 | `IGraphRuntimeApi.TryGetPlacedEntity` 默认 throw + `GasGraphRuntimeApi.BindPlacedInstanceIndexResolver` + GameEngine 注入 session.EntityIndex |
| 编辑器作者面（Placed 面板/拖拽/instanceId 字段） | 2 | `GraphVariablePanel.tsx` / `GasGraphEditorPage.tsx` / `authoredFields.ts`，数据源复用 Bridge instances 端点（零新端点） |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`（Register + `EffectOperationMetadata.Pure`，漏归类即启动抛）；`RequireMapVariableScopeMap` 同款 MapId 解析（`s.MapScope` 优先，caster MapEntity 兜底）。
- Queues / Systems: 无新队列；复用 MapLoader 装载链与 `MapSession.EntityIndex` 生命周期。
- Resolvers / Registries: `MapLoadEntityIndex`（ByInstanceId/TryGet 只读）、`ConfigKeyRegistry`（instance id → key id，同 LoadEntryPayload* 先例）、`GraphProgramSymbolPatcher` intern、`GraphProgramRegistry` 挂载链。
- Existing presets / graphs: gallery 管线（vignette → generate-graph-op-node-galleries.py → per-op showcase + coverage.registry）、Bridge `GET /api/mods/{modId}/maps/{mapId}/instances`。

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadPlacedEntity (416) | E[Dst] := 本地图放置 InstanceId 对应实体；未注册/已死 → Entity.Null（不抛，区别于 LoadEntryPayload* 的 throw 语义，issue 3.3 原文合同） | 现有 op 无"按放置 InstanceId 查实体"通道；LoadEntryPayload* 读的是事件快照载荷，来源与生命周期都不同 |

### 5. Transaction boundary

无多步事务：单条只读查表（index 命中 + World.IsAlive 双保险），无 rollback 需求。

### 6. Config SSOT

行为配置落在: graph 节点字段 `instanceId`（`GraphControlFlowNode.InstanceId` 新字段，不复用 Var/PayloadKey）+ `MapConfig.InstanceExposure`/`WatchedInstances`（可选；declared 分支装载期抛"等 HITL 拍板阈值"，stub fail-closed）。

是否新增 JSON schema: **NO**（MapConfig 增可选字段是既有 schema 增量；graph 文档走既有 strict deserialize）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum（InstanceExposure 是 map 级声明，declared 只收窄变量物化且当前 fail-closed 不落地）
- [x] 未新建与 spawn 平行的物化管线（只读查已有 MapLoadEntityIndex）
- [x] 未把 placement 校验塞进 lifecycle op（校验在挂载层 TriggerGraphMounting）
- [x] 未添加「说不清的」默认 fallback（运行期 miss 写 Entity.Null 是 issue 3.3 明文读合同，非静默 fallback）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / 新 op**（后续 LoadPlacedRegion/Anchor 切片 = 新 op + 新 vignette graph；不改 Core enum 行为开关、不加 profile 开关）。

---

## GAS Composition Gate — Self Review (#1123 / #1124)

- **Task / Issue**: #1123 跨 map 事件通信（global 触发器表 + FireCrossMapEvent + DispatchMapEvent global scope）+ #1124 数据驱动 mod hook（entry priority + Route A 编译期织入 weaver）
- **Date**: 2026-08-24
- **Agent / Author**: ZCode (graph-editor-audit worktree)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 全部能力由既有 DispatchMapEvent 节点的 scope 参数路由、既有 TriggerManager 分发表结构、既有编译/注册基建（`GraphProgramRegistry.ReplaceProgram`、`GraphInstructionSourceMap`）组合表达；无新 enum、无平行 profile DSL、无第二条 VM 管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Global 订阅表 / 挂起摘除 / FireGlobalEvent | 0（TriggerManager 既有分发基建内） | `_globalEventTriggers` 字典 + 复用 `FireTrigger` 同步零分配路径 |
| FireCrossMapEvent（点对点） | 0 | TriggerManager + MapSessions 存在性 fail-closed，只碰 target map 表 |
| DispatchMapEvent scope="global" | 2（graph 节点参数，op 已存在） | 编译期 scope 自洽校验放行 + Flags=2 路由 `FireGlobalEventPayload` |
| scope parser 修复（无 params 保留 scope） | 0 | `CustomEventSchemaParser.TryParse` |
| entry priority | 2 | `TriggerGraphEntryConfig`/`TriggerGraphEntry` 字段 → `Trigger.Priority`（运行时零新代码，AddMapEventTrigger 已按 priority 升序插入） |
| Hook weaver | 0（编译期 post-pass，注册后 mount 前） | `TriggerGraphHookWeaver` + `GraphProgramRegistry.ReplaceProgram` |

### 3. Reuse list

- Handlers: `FireTrigger`（同步零分配路径）、`FireEventHandlers`、`GasGraphOpHandlerTable.HandleDispatchMapEvent`。
- Queues / Systems: `MapHeartbeatClockSystem`（resume companion 不变）；`SetMapEntitiesSuspended`（挂起/恢复唯一挂点，覆盖 Push/Pop/LoadMap/RestoreFocusedMapSession）。
- Resolvers / Registries: `EventSchemaRegistry`（scope SSOT）、`CustomEventSchemaParser`、`GraphProgramRegistry`（Register/ReplaceProgram/TryGetSourceMap）、`GraphIdRegistry`、`ConfigKeyRegistry`、`MapSessionManager.GetSession`。
- Existing presets / graphs: night-raid TriggerGraph 族（回归基线）。

### 4. New Layer 0 ops (if any)

N/A — 无新 graph opcode；global 派发复用 DispatchMapEvent（opcode 453）的 Flags 位。

### 5. Transaction boundary

无 all-or-nothing rollback 需求：注册/摘除均为幂等增量维护；织入失败时 `GraphProgramRegistry.ReplaceProgram` 自带回滚（try/catch 恢复旧 program），编译期整体 fail-closed。

### 6. Config SSOT

行为配置落在: `Events/custom_events.json`（scope/params，既有 schema）+ `GAS/graphs.json` entries（priority/hookAnchor/hookNodeBefore/hookNodeAfter/anchor 字段，扩展既有 authoring shape 白名单）。

是否新增 JSON schema: **NO** — 只在既有 TriggerGraph entry/节点 authoring 上加字段。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（global 表只收 schema.Scope==Global；scope 不匹配 mount/编译期 fail closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**（新订阅 = 新 entry + scope 声明；新 hook = 新 hookAnchor 字段指向 anchor 节点）。
