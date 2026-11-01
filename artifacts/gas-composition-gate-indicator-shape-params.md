## GAS Composition Gate — Self Review

- **Task / Issue**: 指示器形状参数与圆心；尺寸改由技能 exec 放出的效果取数
- **Date**: 2026-10-30
- **Agent / Author**: cursor agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（一个只读 op）+ 现有 presenter 绑定

结论: PASS

一句话理由: 形状图按节点 key 里的字段列表，从这招 `exec` 放出的效果上读已有的 float。技能不再声明「谁来读它」。绑定上的 logicCm 与世界坐标同一条厘米到米换算。圆心是 presenter 锚点上的黑板坐标。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 按字段列表从技能 `exec` 的 EffectClip / EffectSignal 效果里读一个 float，按字段顺序取第一个有值的，同一字段两个效果都带就失败 | 0 | `LoadAbilityEffectFloat`（552） |
| 具名 float 的字段表、列表校验与取数 | 0 | `EffectTemplateNamedFloat`（编译期、装载期、运行时共用） |
| 图挂到技能上时按这个技能查一遍 | 0 | `TriggerGraphMounting.RequireEffectFloatBinding` |
| 逻辑厘米换成视觉米 | 2 | presenter 绑定 `unit: logicCm`，`PresenterBehaviorSystem` 调用 `WorldUnits.CmToM` |
| 地面贴片圆心 | 2 | presenter `anchor.worldPosition` → 黑板 float → `EmitGroundOverlayAsset` |
| 线 / 圆 / 锥把读数写入 `aim.shape.length` / `width` / `radius` / `angle` | 数据 | `graph.aim.shape.*`，节点 key 写字段列表 |
| 重绑时保住 `aim.slot` | 数据 | `graph.aim.read_held_slot` |
| 换演员、先手没命中时按当前槽位技能重开形状 | 数据 | `graph.aim.refresh_gesture`、`graph.aim.target_first` 派 `Aim.Shape.Bind` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 转发，与 `LoadAbilityCastRange` 同一类纯读
- Queues / Systems: 无新队列；地面贴片仍走 `PresenterAssetEmitRuntime`
- Resolvers / Registries: `AbilityDefinitionRegistry`（`ExecSpec`）、`EffectTemplateRegistry`、`EffectTemplateIdRegistry`、`OrderBlackboardKeyRegistry`、`GraphIdRegistry`
- Existing presets / graphs: 形状图、`aim.range` 射程圈、效果模板上已有的 radius / halfAngle / halfWidth / length / projectile.range / collisionHalfWidth / placementRadiusCm、`WorldUnits.CmToM`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadAbilityEffectFloat (552) | 读槽位技能 `exec` 放出的效果上、节点字段列表点名的一个 float | `LoadConfigFloat` 只在效果配置上下文里有数；`LoadAbilityCastRange` 只读 `castRangeCm`。按形状各写一个 op 会把线宽、半角、半径拆进 Core。551 是 `ApplyMapDefaultCamera` |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。该 op 不写世界，不改生命周期。

### 6. Config SSOT

行为配置落在: effect template（半径、半角、半宽、长度、弹道射程、弹道半宽、放置半径）+ ability `exec`（放出哪些效果）+ 形状图节点 key（读哪些字段、什么顺序）+ presenter 绑定（黑板键、`unit: logicCm` → 贴片参数 / 圆心）

是否新增 JSON schema: NO — 删掉了 `effectFields` 和 `targeting.impactEffect`；节点 key 仍是字符串，只是允许 `|` 连接多个封闭字段名。presenter 绑定的 `unit` 沿用。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（读不到、两个效果都带同一字段都直接失败）

### 8. Next variant test

「下一个 Mod 变体」将修改: effect 步骤（技能 `exec` 放出别的效果，或效果模板上的那个数）/ graph 节点 key（换字段或顺序）

若选了 Core enum → FAIL
