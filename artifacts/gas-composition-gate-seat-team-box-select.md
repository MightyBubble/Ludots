## GAS Composition Gate — Self Review

- **Task / Issue**: 框选名单按座位上的人查队伍，去掉写死的队伍号
- **Date**: 2026-11-13
- **Agent / Author**: bc-ab1afa84-f68c-52d5-bb77-cb64c009f92a

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 沿用已有的 LoadCaster 与 QueryFilterTeam，用队伍来源操作数表示「取这个实体当前的队伍」，再按已有的唯一座位合同读队伍。没有新的图操作，也没有新的配置枚举。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 框选候选人从谁的队伍里留 | 1 | `graph.mass_navigation.selection_candidates`：LoadCaster → QueryFilterTeam 的 source |
| 队伍来源三种写法互斥 | 0 | 现有 QueryFilterTeam 的 flags：立即数、整数引脚、实体引脚 |
| 座位上的人是谁、他的队伍号 | 0 | `ClientLocalSeatRegistry.RequireSolePossessedRep` 与 `Team` |
| 换队后名单更新 | 0 | 已有 `DerivedEntityIndex`，队伍号变了就重扫，不重新绑名单 |

### 3. Reuse list

- Handlers: `HandleQueryFilterTeam`、`HandleLoadCaster`、`FilterTeam`
- Queues / Systems: 无
- Resolvers / Registries: `ClientLocalSeatRegistry`、`ClientLocalSeatAccess.RequireSoleSeatTeamId`
- Existing presets / graphs: `graph.mass_navigation.selection_roster` 仍把这份名单绑到 `case_e.selectable`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。座位上的人没有队伍，或座位不是恰好一个时，名单绑定在写入集合之前失败。

### 6. Config SSOT

行为配置落在: graph（`mods/capabilities/navigation/MassNavigationMod/assets/GAS/graphs/graph.mass_navigation.selection_candidates.json`）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

换一个座位或换一支要跟随的人，改的是图上的 source 连线，不是 Core 里的队伍号。
