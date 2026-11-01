# GAS Composition Gate — DispatchMapEvent 提交次数

- **Task / Issue**: `DispatchMapEvent` 在同步派发结束时给出整数，等于监听方调用 `SubmitCast` 与 `SubmitCommandIntent` 的次数；先选目标图在次数为 0 时打开点击瞄准
- **Date**: 2026-10-14
- **Agent / Author**: cloud agent

## GAS Composition Gate — Self Review

- **Task / Issue**: `DispatchMapEvent` 整数输出 + `graph.aim.target_first` 连线
- **Date**: 2026-10-14
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新开 op、不新开枚举；已有 `DispatchMapEvent` 多一个整数输出，先选目标用现有比较和 `ActivateContext` 接上。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 派发期间的提交计数 | 0 | `GasGraphRuntimeApi` 上按嵌套深度分槽的计数，`HandleDispatchMapEvent` 写入 `I[Dst]` |
| 次数为 0 打开点击瞄准 | 2 | `graph.aim.target_first` 连到已有的 `DeactivateOwnContext` + `ActivateContext` |
| 规则图 | 2 | 不改 |

### 3. Reuse list

- Handlers: `HandleDispatchMapEvent`、`HandleSubmitCast`、`HandleSubmitCommandIntent`
- Queues / Systems: `CommandIntentSubmissionBuffer` 的 `Push` / `PushCast`；`TriggerManager.FireMapEvent` / `FireGlobalEvent` 仍同步跑完监听方
- Resolvers / Registries: `GraphOpDescriptorTable` 的线性输出类型；`EventSchemaRegistry` 不变
- Existing presets / graphs: `graph.aim.target_first` 里没有目标的那条 `deact_miss` → `act_click` 分支

### 4. New Layer 0 ops (if any)

N/A。没有新 op。计数挂在已有派发上，因为图在派发指令返回前必须读到次数，而监听方的提交发生在这次同步 `Fire` 里面。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。计数不进世界状态，派发抛错时槽位弹出，不写回放。

### 6. Config SSOT

行为配置落在: graph（`mods/capabilities/input/AimInteractionMod/assets/GAS/graphs/graph.aim.target_first.json`）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
