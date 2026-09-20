# GAS Composition Gate — Issue #1099 收口（TriggerGraph/Dialogue 统一 QueryGraphGateway 回归证据）自审

## GAS Composition Gate — Self Review

- **Task / Issue**: #1099 收口：确认「TriggerGraph/Dialogue 统一 QueryGraphGateway」合同（显式 subject + pins、目标必须已登记 GraphKind.Query、typed Bool/Int/Float/Entity/EntitySet、缺失/类型不符失败关闭、禁止 Query 动作/事件/Store/continuation、不新增第二 VM/bus/条件解释器）已被 main 覆盖；补最小 gateway 回归证据（gateway 拒非 Query 登记图 + Query 策略拒事件/Store/continuation）+ 图能力状态页一行收口说明
- **Date**: 2026-08-26
- **Agent / Author**: pi closeout session

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: D——本收口不新增任何能力变体。统一 Query 网关就是主干 GraphReturnWriter（引擎服务 + GraphReturnWriterPanelEvaluator 消费），合同逐条已落地：

- 显式 subject + pins：`ExecuteAndWrite(graphId, owner, caster, explicitTarget, targetContext, targetPosCm, randomSeed, api)` + 输出 schema 的寄存器 pin 绑定
- 目标必须已登记 GraphKind.Query：GraphReturnWriter 首道门 `_programs.RequireKind(graphId, GraphKind.Query)`
- typed Bool/Int/Float/Entity/EntitySet：GraphOutputValueKind 四类 summary + TargetList 集合输出
- 缺失/类型不符失败关闭：缺 schema/缺 owner+caster 抛异常；输出类型与源节点类型不符在编译期 TypeMismatch 失败关闭
- 禁止 Query 动作/事件/Store/continuation：操作策略（GasTransactional 动作/事件/Store 拒、ScriptSliceOnly 续延拒）+ 作者化 mask（WriteMapVar*/SpawnTemplate 等 Query 不可作者化）+ Query 编译器 op 白名单
- 不新增第二 VM/bus/条件解释器：TriggerGraph 与 Query 共用 GraphFrame/GraphExecutor 单 VM；TriggerGraph 挂载走 ExecuteScriptSlice（Script 切片），Query 只经 GraphReturnWriter，互不新造解释器

已有测试覆盖：Query 输出集合/摘要写入、缺 schema 失败关闭、缺 subject 失败关闭（#1084 收口）、Query 策略拒动作写（ModifyAttributeAdd）。本次补两条缺口回归：① gateway 拒已登记 TriggerGraph 程序（目标必须是 Query）；② Query 策略拒事件（SendEvent）/Store（WriteBlackboardFloat）/续延（Yield）。

结论: PASS

一句话理由: 没有新 graph 节点、新 effect 步骤、新 profile enum、新预设开关、新网关或平行管线；两条测试只断言既有 GraphReturnWriter/GraphKindOperationPolicy 的失败关闭行为，未改任何 Core 业务实现。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| gateway 拒非 Query 登记图回归 | 测试 | GraphReturnWriter_RejectsRegisteredTriggerGraphKind_FailsClosed（EntitySetQueryRuntimeTests：登记 TriggerGraph 程序 + 走 ExecuteAndWrite，断言 kind 门拒绝） |
| Query 策略拒事件/Store/续延回归 | 测试 | GraphKindOperationPolicy_QueryRejectsEventStoreAndContinuation（EntitySetQueryRuntimeTests：SendEvent/WriteBlackboardFloat/Yield 三用例） |
| 状态页 #1099 收口同步 | 文档 | gitbook/architecture/graph-capability-status.md §3.2 第五件 + 附录编号 |

### 3. Reuse list

- Handlers: 无新增 handler；GasGraphOpHandlerTable.Instance 原样复用
- Queues / Systems: 无新增系统
- Resolvers / Registries: GraphProgramRegistry / GraphOutputSchemaRegistry / GraphOutputValueStore / GraphIdRegistry 原样复用（测试只引用，不修改）
- Existing presets / graphs: 无新增图配置；TriggerGraph 测试程序直接以指令数组登记（含 TriggerGraph entry 表）

### 4. New Layer 0 ops (if any)

N/A——零新 opcode、零新事件键、零新 schema 字段、零新网关/总线/解释器。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新增多步事务。测试断言失败关闭发生在任何查询执行与输出写入之前（kind 门在 RequireAllowed/schema 之前；策略门在任何 opcode 执行之前），不留半写输出。

### 6. Config SSOT

行为配置落在: 无新增配置。Query 图输出与集合写入沿用既有 GraphReturnWriter 与 GraphKindOperationPolicy 合同。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（kind/策略不符直接抛异常，绝不降级执行）
- [x] 未复制 Graph VM / 第二事件总线 / 第二条件解释器（TriggerGraph 与 Query 仍共用 GraphFrame/GraphExecutor 单 VM）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（例如新的 Query 图只是既有 op 的新连线与输出绑定，无需 Core enum）

若选了 Core enum → FAIL
