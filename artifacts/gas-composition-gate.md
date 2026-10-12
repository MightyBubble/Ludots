# GAS Composition Gate — Self Review

- **Task / Issue**: Slice F — `targeting.targetKinds` token `none`
- **Date**: 2026-10-12
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: `none` 是现有 `AbilityAcceptsTargetKind` 多读的一个数据符号，加上 `graph.aim.resolve_submit` 的边：成立就 `SubmitCast` 只带槽位。没有新 opcode，没有新 profile enum。`castRangeCm` 为 0 不会自动变成 `none`。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 问这一槽是否声明了 `none` / `unit` / `ground` | 0 | 现有 `AbilityAcceptsTargetKind` |
| `none` 独占，和 unit/ground 同列则加载失败 | 2 的加载 | `AbilityExecLoader.CompileTargetKinds` |
| `none` 成立时忽略瞄准状态，只提交槽位 | 2 | `graph.aim.resolve_submit` |
| 哪个技能是无目标施放 | 2 的数据 | `abilities.json` `targeting.targetKinds` |

### 3. Reuse list

- Handlers: 无新 BuiltinHandler
- Queues / Systems: 无
- Resolvers / Registries: `AbilityDefinitionRegistry`、`AbilityTargetKinds`、`ConfigKeyRegistry`
- Existing presets / graphs: `graph.aim.resolve_submit` 的 `SubmitCast`（不接 target、不接 condition）

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。读取发生在提交之前；`none` 路径不写瞄准点。

### 6. Config SSOT

行为配置落在: `targeting.targetKinds`（ability JSON）+ `graph.aim.resolve_submit`

是否新增 JSON schema: NO — 仍是已有 `targetKinds` 数组。新符号 `none` 独占，不新开字段，也不从 `castRangeCm` 推断。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / ability JSON 的 `targetKinds`

未改 Core enum。`none` 与 `unit` / `ground` 一样是数据符号。
