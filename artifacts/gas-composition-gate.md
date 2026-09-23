## GAS Composition Gate — Self Review

- **Task / Issue**: #1394 Activity v5 设计定稿 + Record 通用事件日志
- **Date**: 2026-08-30
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次新增的是 Graph VM 可组合的 Record 读写/查询节点和既有 Activity 图入口，行为差异落在图连线与参数上，不新增 profile enum、preset 开关或平行解释管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| RecordLog 环形存储 | 0 | ECS 组件与无分配边界明确的追加/查询 API |
| WriteRecord | 2 | 现有 GraphNodeOp、GasGraphOpHandlerTable、IGraphRuntimeApi |
| QueryRecordCount | 2 | 现有 Validation 图输出与 Graph VM 整数寄存器 |
| QueryRecordExists | 2 | 现有 Validation 图输出与 Graph VM 布尔寄存器 |
| Activity 条件/结算图入口 | 2 | 现有 GraphProgramRegistry、GraphExecutor、IGraphRuntimeApi |
| Record 持久化 | 1 | 现有 persistence domain/formatter 注册链路 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 内置 handler 注册表；既有 `GraphNodeOp` 编码与符号解析。
- Queues / Systems: 现有 GraphExecutor 执行链；现有 persistence domain 注册和 formatter 链路；ActivityRuntimeService 的实体实例生命周期。
- Resolvers / Registries: `GraphOpDescriptorTable`、`GraphProgramRegistry`、`ConfigKeyRegistry`、现有 `IGraphRuntimeApi`/`GasGraphRuntimeApi`。
- Existing presets / graphs: 既有 Script/TriggerGraph/Validation 图契约；不新增 Record 专用求值器或面板管线。

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | RecordLog 是数据组件，不是实体生命周期 atomic op | Record 追加与按类别查询是新的最小领域能力；通过 Graph VM 暴露，不能用属性写或黑板写伪造持久审计语义 |

### 5. Transaction boundary

必须原子 rollback 的步骤: Record 单条追加必须一次性完成；容量淘汰与新条目写入在同一组件操作内完成。图执行失败时不做静默补写或降级。

### 6. Config SSOT

行为配置落在: graph（`assets/GAS/graphs.json` 及现有 GraphProgramConfigLoader）；Record 的字段由图节点参数和运行时 EntryPayload/上下文提供。

是否新增 JSON schema: NO — Record 不新增平行 catalog；现有图配置、符号注册和持久化 domain 作为 SSOT。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（选 graph 连线）
