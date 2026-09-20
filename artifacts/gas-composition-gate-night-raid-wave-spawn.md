## GAS Composition Gate — Self Review

- **Task / Issue**: #1030 第 7 条：夜袭旗舰波次触发式刷怪
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 波次差异只通过现有 `SpawnTemplate` 节点、TriggerGraph 入口和连线/参数表达，不新增 profile enum、preset 开关或平行 spawn 管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 模板物化 | 0/1（既有能力） | `SpawnTemplate` → 现有 runtime spawn queue |
| wave1/wave2/boss 编排 | 2 | `Graph.NightRaid.Flow` TriggerGraph 数据 |
| 队伍与生命值 | 2 | `Entities/templates.json` 模板组件 |
| 地图位置与镜头 | 2 | `night_raid.json` 地图/相机数据 |

### 3. Reuse list

- Handlers: 既有 `SpawnTemplate` op 447、`WriteMapVarInt`、`ReadMapVarInt`、`AddInt`、`CompareEqInt`、`JumpIfFalse`、`EntityAliveCountChanged` 入口。
- Queues / Systems: 既有 runtime spawn queue、MapHeartbeat/TriggerManager、MapVariableStore、地图 DeathRule destroy 管线。
- Resolvers / Registries: 既有 graph authoring front door、template registry、presentation presenter registry。
- Existing presets / graphs: `Graph.NightRaid.Flow`、`Graph.NightRaid.Panel.Values`，不引入第二事实源。

### 4. New Layer 0 ops (if any)

N/A。没有新增 Layer 0 op。

### 5. Transaction boundary

每个 `SpawnTemplate` 请求继续由既有 spawn queue 负责原子入队/失败处理；图只负责有序组合。不存在新的跨实体事务边界。

### 6. Config SSOT

行为配置落在 graph + entity template + map：
`mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graphs.json`、
`assets/Entities/templates.json`、`assets/Maps/night_raid.json`。

是否新增 JSON schema: **NO**。只使用已有字段和作者面。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**（本票选择 graph 连线）。
