# GAS Composition Gate — Issue #1084 收口（Query 图合同 fail-closed 回归证据）自审

## GAS Composition Gate — Self Review

- **Task / Issue**: #1084 收口：确认 GraphKind.Query 合同（纯读、显式 subject/owner、缺失 subject 失败关闭、精确输出、无 Store/事件/动作/continuation）已被 main 覆盖；补一条唯一缺口的回归测试（GraphReturnWriter 缺失 owner/caster fail-closed）+ 图能力状态页一行收口说明
- **Date**: 2026-08-26
- **Agent / Author**: pi closeout session

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: D——本收口不新增任何能力变体：Query 纯读、显式 subject、精确输出、无 Store/事件/动作/continuation 均已有 main 测试（GraphKindOperationPolicy 拒写 Query、精确摘要/集合值断言、Query 拒未知 op、kind 门拒错配执行入口）；唯一缺口是「缺失 subject fail-closed」没有测试，本次只补这条回归测试并同步状态页。

结论: PASS

一句话理由: 没有新 graph 节点、新 effect 步骤、新 profile enum、新预设开关或平行管线；测试只断言既有 GraphReturnWriter 的 owner→caster 解析与 Null fail-closed 合同，未改任何 Core 业务实现。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 缺失 subject fail-closed 回归测试 | 测试 | GraphReturnWriter_MissingOwnerAndCaster_FailsClosedBeforeExecution（EntitySetQueryRuntimeTests，复用既有 4x cityEconomy Query 图与 GasGraphRuntimeApi 装配） |
| 状态页 #1084 收口同步 | 文档 | gitbook/architecture/graph-capability-status.md §3.2 第四件 + 附录编号 |

### 3. Reuse list

- Handlers: 无新增 handler；GasGraphOpHandlerTable.Instance 原样复用
- Queues / Systems: 无新增系统
- Resolvers / Registries: GraphProgramRegistry / GraphOutputSchemaRegistry / EntityCollectionStore / GraphOutputValueStore / GraphIdRegistry 原样复用（测试只引用，不修改）
- Existing presets / graphs: 复用既有 `tests.graph.4x.cityEconomy` Query 图配置（GraphConfigJson）与其输出 schema

### 4. New Layer 0 ops (if any)

N/A——零新 opcode、零新事件键、零新 schema 字段。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新增多步事务。测试断言 fail-closed 发生在任何查询执行与输出写入之前，不留半写输出（无 collection / summary 写入可观测）。

### 6. Config SSOT

行为配置落在: 无新增配置。Query 图输出与集合写入沿用既有 GraphReturnWriter 与 GraphConfigJson 合同。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺失 owner/caster 直接抛异常，绝不降级为无主写入）
- [x] 未复制 Graph VM / 第二事件总线 / 第二生命周期管线

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（例如新的 Query 图只是既有 op 的新连线与输出绑定，无需 Core enum）

若选了 Core enum → FAIL
