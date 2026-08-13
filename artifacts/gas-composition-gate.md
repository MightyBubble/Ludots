## GAS Composition Gate — Self Review

- **Task / Issue**: GraphOps per-op galleries — playable recordings on the production path
- **Date**: 2026-08-13
- **Agent / Author**: Cursor Grok 4.6

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（现有 graph op 组合，gallery host 接到引擎生产服务）

结论: PASS

一句话理由: 不新增 opcode / preset / profile；修的是画廊宿主怎么把已有节点接到地图实体、坐标系、路网和 HUD 观众上。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 扣血 / 回血 | 0 | 已有 ModifyAttributeAdd / WriteSelfAttribute |
| 拆朋友链 | 0 | 已有 RelationshipRemoveLink |
| 六边形查询 | 0 | 已有 QueryHex* + 引擎 SpatialQueryService |
| 吸到路边 | 0 | 已有 SnapToNearestGraphEdge + LoadedGraphRuntime |
| 观众知识 | 0 | 已有 KnowledgeHasProjection / LoadViewer |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable, BuiltinHandlers
- Queues / Systems: engine EffectRequestQueue, SpatialQueryService, GasGraphRuntimeApi
- Resolvers / Registries: engine EffectTemplateRegistry, Relationship* registries, AttributeRegistry
- Existing presets / graphs: `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/*.json`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。画廊只执行已有 graph；不新开 lifecycle transaction。

### 6. Config SSOT

行为配置落在: graph / vignette / field JSON（`assets/GAS/graphs/`、`assets/Vignettes/`）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤
