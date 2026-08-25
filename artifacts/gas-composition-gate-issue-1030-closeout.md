# GAS Composition Gate — Issue #1030 closeout 自审（scope-less TriggerGraph 挂载 fail-closed 修复 + UAT 收口测试）

## GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1030 实现收口：证据审计 + 修复 scope-less TriggerGraph 挂载执行时以 Entity.Null 触碰 Arch 原生内存（AccessViolation），补 UAT 2「缺 scope 时 fail closed」回归测试与状态页同步。（本文件不覆盖 `artifacts/gas-composition-gate.md`（图编辑器里程碑正本）与 `gas-composition-gate-map-trigger-closeout.md`（收口批正本）；按 §3.5 后开活不覆盖既有正本。）
- **Date**: 2026-08-27
- **Agent / Author**: pi closeout agent（worktree `.pi-worktrees/issue-1030`）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A——既有执行管线的缺陷修复（fail-closed 守卫），不是新变体**

结论: **PASS**

一句话理由: 最新 main 已在 `TriggerGraphMountTrigger.ResolveMapScopeOnce` 保留 scope 缺失/死亡实体守卫；本次补齐 #1030 的 scope-less 地图变量 fail-closed 回归证据，不新增 profile enum、preset 开关、opcode、执行器或第二套 VM；守卫前 scope-less 挂载执行会以原生 AccessViolation 崩掉测试宿主/进程，违反史诗「缺 scope 时 fail closed」合同。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| scope-less 挂载的地图作用域解析守卫 | 0（既有 TriggerGraph 执行入口） | `TriggerGraphMountTrigger.ResolveMapScope`：`Entity.Null/default/dead → null`，与 `IsScopeDispatchable`/`ResolveRunCaster` 既有守卫同一模式 |
| 地图变量 op 缺作用域 fail-closed | 0（既有） | `GasGraphOpHandlerTable.RequireMapVariableScopeMap` → `GAS.GRAPH.ERR.MapVariableScopeEntity`（未改动，修复后真正可达） |
| UAT 2 回归测试 | 测试 | `TriggerGraphMountTests.ExecuteAsync_ScopeLessMount_WithMapVariableOp_FailsClosedNamingOp` |

### 3. Reuse list

- Handlers: 既有 `RequireMapVariableScopeMap`（Read/WriteMapVarInt/Float 共用），未新增 op handler。
- Queues / Systems: 既有 TriggerManager / TriggerGraphMountTrigger 执行切片管线，未新增。
- Resolvers / Registries: 既有 `MapEntity` 组件解析、`GraphProgramRegistry`、`GraphExecutor.ExecuteScriptSlice(mapScope:)` 参数，未新增。
- Existing presets / graphs: 夜袭旗舰与 override mod 数据零改动（生产挂载全部带 scopeInstanceId，行为不变）。

### 4. New Layer 0 ops (if any)

N/A — 未新增 opcode；修复在既有 op 的执行入口。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。守卫是纯读取（null 短路），不引入事务面；destroy-tick 生命周期派发（实体已销毁但组件可读）的 map scope 解析路径保持不变——守卫刻意不 gate `IsAlive`，避免回退 `3f75d98e3c` 修复的死亡事件 map scope 合同。

### 6. Config SSOT

行为配置落在: graph（TriggerGraph entries）与 map（TriggerGraphs 挂载），零新增 JSON schema。

是否新增 JSON schema: NO。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback——scope 缺失或实体已死时地图变量 op 明确抛 `GAS.GRAPH.ERR.MapVariableScopeEntity`，非静默失败

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（scope-less 挂载 + 地图变量 op 的图数据）——作者面不变，契约已由回归测试钉住。
