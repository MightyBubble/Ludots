## GAS Composition Gate — Self Review

- **Task / Issue**: 瞄准图写空实体句柄，不再用 TargetListGet 下标 99
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 现有节点写不出 `Entity.Null`。新增一个只写空句柄的 Layer 0 节点，瞄准图改接它。越界的 `TargetListGet` 仍按原合同写有效位 0，因为空名单取第 0 个还靠这个结果。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 写下空实体 | 0 | `ConstNullEntity`（opcode 8），`E[Dst] = Entity.Null` |
| 准备、清除、跟踪里清目标 | 2 | `graph.aim.prepare_actor` / `clear` / `track_pointer.body` / `track_stick.body` / `apply_stick` 把原先的下标 99 换成这颗节点 |

### 3. Reuse list

- Handlers: 图执行表里的常量节点写法，和 `LoadCaster` 同一类：无值边，结果进实体寄存器
- Queues / Systems: 无
- Resolvers / Registries: `GraphOpDescriptorTable`、`GraphNodeOp` 覆盖登记、节点画廊
- Existing presets / graphs: 瞄准图里真正按下标取名单的 `TargetListGet` 不动

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ConstNullEntity | 把 `Entity.Null` 写入实体寄存器 | 没有任何节点产出空句柄。`default(Entity)` 是 `{0,0,0}`，存进黑板后过不了保存校验。用越界 `TargetListGet` 冒充空句柄，是在借失败结果 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。这颗节点不改世界。

### 6. Config SSOT

行为配置落在: graph

- `mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.prepare_actor.json`
- `mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.clear.json`
- `mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.track_pointer.body.json`
- `mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.track_stick.body.json`
- `mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.apply_stick.json`

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

再有一处要写空实体，接 `ConstNullEntity`，不新加 opcode。
