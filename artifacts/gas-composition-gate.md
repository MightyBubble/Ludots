## GAS Composition Gate — Self Review

- **Task / Issue**: GraphOps per-op galleries — retire dual-world / C# spawn / fake config; people and graphs go through the production map + GAS graph path
- **Date**: 2026-08-13
- **Agent / Author**: Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 不新增 opcode / profile DSL；把画廊从「代码里再造一套人」收回 `MapLoader` + 生产 `GasGraphRuntimeApi`，HUD 只绑地图已有实体。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 人从地图进出 | 3 | `MapLoader.LoadTemplates` / `LoadEntitiesAndIndex` |
| 图执行 | 2 | `GasGraphRuntimeApi`（引擎生产服务或同型装配） |
| 配置威力/阶位 | 2 | `assets/GAS/effects.json` `Effect.GraphOps.Config` + `SetConfigContext` |
| 花名册 / 关系 / 标签 | 2 | `EntityCollectionStore` / `RelationshipRuntime` / `TagOps`，数据在 vignette |
| HUD 血条 | 3 | 对地图实体 disclose，不再 `Stage.Spawn` 第二套人 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`（已有 120 op）
- Queues / Systems: `MapLoader`, `SpatialPartitionUpdateSystem`, `EffectRequestQueue`, `GameplayEventBus`
- Resolvers / Registries: `EntityTemplateKeyRegistry`, `Relationship*Registry`, `TargetDispatchPresetRegistry`, `EntityCollectionStore`, `EffectTemplateRegistry`
- Existing presets / graphs: per-op FrontDoor graphs unchanged

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无跨帧 Effect transaction。地图加载失败、缺 InstanceId、图未 Halt 均 fail-close。

### 6. Config SSOT

行为配置落在: gallery `assets/GAS/graphs/{Op}.json` + `assets/GAS/effects.json` + vignette/map Entities

是否新增 JSON schema: **NO** — vignette 只补齐已有演员字段（team/tags/collections/links），地图仍是 `EntitySpawnData`

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（删掉 `World.Create` 演员；HUD 不再当出生点）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / vignette 演员与地图**
