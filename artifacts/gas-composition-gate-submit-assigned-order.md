# GAS Composition Gate — SubmitAssignedOrder 可选目标

- **Task / Issue**: 行为图 `SubmitAssignedOrder` 在目标口未接线时，按订单合同把目标写成 `Entity.Null`
- **Date**: 2026-09-24
- **Agent / Author**: cloud agent

## GAS Composition Gate — Self Review

- **Task / Issue**: SubmitAssignedOrder 未指目标则填 Entity.Null
- **Date**: 2026-09-24
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增节点、枚举或配置开关，只让已有下单节点的目标口可以不接，缺省落成订单合同已有的空实体。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 目标口可不接 | 0 | 已有 `SubmitAssignedOrder` 的编译、寄存器校验、handler |
| 落点坐标与订单类型 | 2 | 图连线 `a`/`b` 与 `orderType`，仍必填 |
| 入队 | 0 | 已有 `OrderQueue.TryEnqueueAssigned` |

### 3. Reuse list

- Handlers: `HandleSubmitAssignedOrder`；对照 `HandleSubmitCommandIntent` 的「未接线 = byte.MaxValue」，不抄它的死亡实体折叠
- Queues / Systems: `OrderQueue`
- Resolvers / Registries: `OrderTypeRegistry`、`GraphProgramRegistry.Register` → `ValidateRegisterBounds`
- Existing presets / graphs: 画廊与前线图继续接目标，不改连线

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。未接线在编译期收成缺省标记；入队失败仍按现有队列满/未知订单类型/缺拥有者直接抛出。

### 6. Config SSOT

行为配置落在: graph（作者图的值边）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未接线是作者显式省略；接上的实体原样写入，死实体不改成空）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
