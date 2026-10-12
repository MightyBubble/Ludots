# GAS Composition Gate — Self Review

- **Task / Issue**: 技能瞄准规则改由技能引用的共享 TriggerGraph 决定
- **Date**: 2026-10-12
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 按下、确认、松开只读出槽位上的技能编号，再把队列修饰符派给这颗技能自己挂上的规则图。规则图是共享数据，多颗技能引用同一张。Core 不解释这颗技能打什么。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读出槽位上的技能编号 | 0 | `LoadSlotAbilityId` |
| 把事件派到该技能挂上的图 | 0 | `DispatchMapEvent` 的 ability route（传输元数据 `MapTrigger.AbilityId`，施法者写入 `SourceEntity`） |
| 问某个情境是否还开着 | 0 | `IsContextActive` |
| 无目标 / 只单位 / 只地面 / 单位或地面 | 2 | `graph.aim.rule.none`、`graph.aim.rule.unit_only`、`graph.aim.rule.ground_only`、`graph.aim.rule.unit_or_ground` |
| 哪颗技能用哪条规则 | 2 的数据 | `abilities.json` 的 `triggerGraphs` |
| 槽位上的技能没有规则图接住 | 2 | 规则图写 `aim.rule.handled`，按下图读不到就 `Fail` |

### 3. Reuse list

- Handlers: `DispatchMapEvent` 增加 ability route，不新开事件种类
- Queues / Systems: `TriggerManager.FireMapEvent` 同步派发；技能域挂载已有的 `AbilityId` 过滤
- Resolvers / Registries: `AbilitySlotResolver`（槽位解析不重写）、`EventSchemaRegistry` 的传输元数据
- Existing presets / graphs: `graph.aim.resolve_submit`、`graph.aim.indicate`、选中单位改绑的 hook

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `LoadSlotAbilityId` | 演员 + 槽位索引 → 技能编号 | `QueryCollectAbilitySlots` 收集槽位列表，不按索引返回技能编号 |
| `IsContextActive` | 主体是否带着命名情境 | 没有读取情境是否激活的 op |
| `DispatchMapEvent` ability route | 派发时带上技能编号 | 自定义事件 schema 不能声明保留的 `MapTrigger.` 命名空间，技能编号不能做成事件参数 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。按下图先把 `aim.rule.handled` 写成 0；规则图在自己的切片里写成 1。没有挂载接住时按下图 `Fail`。规则图对不上施法模式时自己 `Fail`，异常穿出派发，不吞掉。

### 6. Config SSOT

行为配置落在: AimInteractionMod 的 `graph.aim.rule.*`，以及各技能 `abilities.json` 的 `triggerGraphs`。交接事件 `Aim.Rule.Press` / `Aim.Rule.Resolve` 是该 Mod 的 `Events/custom_events.json`。

是否新增 JSON schema: NO — 事件走已有 custom event 通道，Core 技能定义不新增字段。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

新的瞄准规则是再挂一张图，或让技能改引用另一张已有的图。不改 Core enum。
