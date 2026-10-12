# GAS Composition Gate — Self Review

- **Task / Issue**: Slice F review — ability aim target kinds
- **Date**: 2026-10-12
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 交付物是一颗只读技能声明的 Layer 0 节点，加上既有 `targeting` 块上的数据字段；图自己决定“没单位就停”或“忽略单位改走地面”。没有新的 profile enum，Core 不按游戏分支。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读某一槽技能是否接受 unit 或 ground | 0 | `AbilityAcceptsTargetKind` |
| 技能声明可接受的种类 | 2 的数据 | `abilities.json` `targeting.targetKinds` |
| 有单位才提交、只收地面就忽略 `aim.target` | 2 | `graph.aim.resolve_submit` |

### 3. Reuse list

- Handlers: 无新 BuiltinHandler
- Queues / Systems: 无
- Resolvers / Registries: `AbilityDefinitionRegistry`、`AbilitySlotResolver`、`ConfigKeyRegistry`
- Existing presets / graphs: `graph.aim.resolve_submit`；删除 `graph.interaction.ground_slot` 与 `graph.moba.no_ground_cast`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| AbilityAcceptsTargetKind | 回答“这一槽的技能是否声明了这一种目标” | 技能定义不在图寄存器里。`LoadAbilityCastRange` 只读距离。没有现成节点能读目标种类。 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。节点只读。

### 6. Config SSOT

行为配置落在: `targeting.targetKinds`（ability JSON）+ `graph.aim.resolve_submit`

是否新增 JSON schema: YES — 在已有 `targeting` 对象上增加 `targetKinds`。不新开 profile 文件。`targeting` 已经是施法距离和命中效果的事实源；目标种类是同一份声明，不是另一套瞄准配置。带 `targeting` 却不写 `targetKinds` 的技能在加载时失败，不默认成“单位和地面都收”。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / ability JSON 的 `targetKinds`

未改 Core enum。`unit` / `ground` 是数据符号，节点做字符串比较。
