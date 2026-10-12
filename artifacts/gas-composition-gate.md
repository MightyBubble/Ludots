# GAS Composition Gate — Self Review

- **Task / Issue**: Slice F — `["none"]` 技能在任何施法模式都按下即提交
- **Date**: 2026-10-12
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: `graph.aim.press` 在施法模式分支之前用现有 `AbilityAcceptsTargetKind`（符号 `none`）判断，成立则先转发队列修饰符再 `InvokeGraph` 已有的 `graph.aim.resolve_submit`。不新开 opcode，不新开 profile enum，也不从 `castRangeCm` 推断。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 问这一槽是否声明了独占的 `none` | 0 | 现有 `AbilityAcceptsTargetKind` |
| `none` 成立则跳过 click / release / smart | 2 | `graph.aim.press` 的 `none_jump` |
| 把本拍队列修饰符交给子图 | 0 | 现有 `LoadEntryPayloadInt` + `StoreArgInt`（`MapTrigger.Modifiers`） |
| 无目标提交（只带槽位） | 2 | 已有 `graph.aim.resolve_submit` |
| 哪个技能是无目标施放 | 2 的数据 | `abilities.json` `targeting.targetKinds` |

### 3. Reuse list

- Handlers: 无新 BuiltinHandler
- Queues / Systems: 无
- Resolvers / Registries: `AbilityDefinitionRegistry`、`AbilityTargetKinds`
- Existing presets / graphs: `graph.aim.press`、`graph.aim.resolve_submit`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。`none` 路径不写瞄准点、不打开瞄准上下文；提交仍由已有 `SubmitCast` 完成。

### 6. Config SSOT

行为配置落在: `graph.aim.press` 连线 + `targeting.targetKinds`

是否新增 JSON schema: NO — 仍是已有图节点与 `targetKinds` 符号。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

未改 Core enum。`none` 在 smart、aim_click、aim_release 都走同一条按下即提交的边。
