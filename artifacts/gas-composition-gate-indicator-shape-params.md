## GAS Composition Gate — Self Review

- **Task / Issue**: 指示器形状参数与圆心（review of indicator shapes, a63862c4f8）
- **Date**: 2026-10-14
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（一个只读 op）+ 现有 presenter 绑定

结论: PASS

一句话理由: 形状参数按节点 key 读技能冲击效果上已有的 float，或 effectFields 里的正数。绑定上的 logicCm 与世界坐标同一条厘米到米换算。圆心是 presenter 锚点上的黑板坐标。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 按当前图和节点 key 从技能 effectFields 读冲击效果上的具名 float | 0 | `LoadAbilityEffectFloat`（552） |
| 具名 float 的字段表，含 `projectile.range` | 0 | `EffectTemplateNamedFloat`（op 与装载校验共用） |
| 技能按图声明一组键，每键是效果字段名或正数 | 数据 | `abilities.json` `effectFields` |
| 逻辑厘米换成视觉米 | 2 | presenter 绑定 `unit: logicCm`，`PresenterBehaviorSystem` 调用 `WorldUnits.CmToM` |
| 地面贴片圆心 | 2 | presenter `anchor.worldPosition` → 黑板 float → `EmitGroundOverlayAsset` |
| 线 / 圆 / 锥把读数写入 `aim.shape.length` / `width` / `radius` / `angle` | 数据 | `graph.aim.shape.*` |
| 重绑时保住 `aim.slot` | 数据 | `graph.aim.read_held_slot` |
| 换演员后按当前槽位技能重开形状 | 数据 | `graph.aim.refresh_gesture` 派 `Aim.Shape.Bind`，形状图增加这个事件入口 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 转发，与 `LoadAbilityCastRange` 同一类纯读
- Queues / Systems: 无新队列；地面贴片仍走 `PresenterAssetEmitRuntime`
- Resolvers / Registries: `AbilityDefinitionRegistry`、`EffectTemplateRegistry`、`OrderBlackboardKeyRegistry`、`GraphIdRegistry`
- Existing presets / graphs: 形状图、`aim.range` 射程圈、效果模板上已有的 radius / halfAngle / halfWidth / length / projectile.range / collisionHalfWidth / placementRadiusCm、`WorldUnits.CmToM`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadAbilityEffectFloat (552) | 读当前技能冲击效果上、由该技能为当前图和节点 key 点名的一个 float | `LoadConfigFloat` 只在效果配置上下文里有数；`LoadAbilityCastRange` 只读 `castRangeCm`。按形状各写一个 op 会把线宽、半角、半径拆进 Core。551 是 `ApplyMapDefaultCamera` |

（无则写 N/A）

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。该 op 不写世界，不改生命周期。

### 6. Config SSOT

行为配置落在: effect template（半径、半角、半宽、长度、弹道射程、弹道半宽、放置半径）+ ability `effectFields`（哪张图的哪个键读哪个字段或正数）+ presenter 绑定（黑板键、`unit: logicCm` → 贴片参数 / 圆心）

是否新增 JSON schema: YES — `effectFields` 的值是对象，键是图节点的 `key`，值是封闭字段名或正数。presenter 绑定增加 `unit`。`triggerGraphs` 保持字符串：效果图不能当触发图挂载，字段名不能塞进图 id。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（技能改 `effectFields` 的键指向的字段，或效果模板上的那个数）

若选了 Core enum → FAIL
