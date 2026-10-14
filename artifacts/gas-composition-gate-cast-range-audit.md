# GAS Composition Gate — cast range audit

## GAS Composition Gate — Self Review

- **Task / Issue**: graph-capability-status §3.3.2 待查：`targeting.castRangeCm` 与 `LoadAbilityCastRange`（534），以及 `context_groups.json` 的 `requiresTarget`
- **Date**: 2026-10-14
- **Agent / Author**: Cursor Agent（worktree `/tmp/wt-cast-range-audit`，`cursor/cast-range-audit-de53`）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（沿用已有 op 534 与 `CompositeOrderPlanner`，不新增节点、不新增枚举）

结论: **PASS**

一句话理由: 走近再施放要的是技能定义上的厘米数。效果模板参数和查找表是别的存储，没有能读技能定义字段的通用 op，所以 534 继续读 `castRangeCm`。`requiresTarget` 是上下文组的打分形状，和瞄准规则图各管一件事。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读槽位技能的 `castRangeCm` | 0（已有） | `LoadAbilityCastRange`（534） |
| 超距先移动再施放 | Core 订单规划（已有） | `CompositeOrderPlanner` |
| 瞄准图把射程写入 `aim.range_cm` | 2 | `graph.aim.indicate` / `resolve_*` 调用 534 |
| 上下文组要不要按实体打分 | 既有配置 | `GAS/context_groups.json` 的 `requiresTarget` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable.HandleLoadAbilityCastRange`
- Queues / Systems: `CompositeOrderPlanner`、`OrderQueue`
- Resolvers / Registries: `AbilityDefinitionRegistry`、`AbilitySlotResolver`、`ContextGroupRegistry`、`ContextScoredOrderResolver`
- Existing presets / graphs: `LoadConfigFloat`、`TableReadFloat`（存储不同，不拿来顶替 534）；`graph.aim.rule.*`（瞄准种类，不携带射程）

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | 不新增 | `LoadConfigFloat` 读效果模板参数，`TableReadFloat` 读查找表。技能定义上没有第三份可按字段名读取的存储 |

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A。装载期缺 `castRangeCm` 或负数直接失败；运行期 534 对空槽、未安装技能、缺字段直接失败。

### 6. Config SSOT

行为配置落在: `GAS/abilities.json` 的 `targeting.castRangeCm`（计划器与 534 的同一份数）；`GAS/context_groups.json` 的 `requiresTarget`（打分形状）

是否新增 JSON schema: **NO**

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺字段失败；0 与没有 targeting 块是「这一单不走近」，原单照原样入队）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（瞄准图继续调用 534）或能力 JSON 里的 `castRangeCm`。不改 Core enum。
