# GAS Composition Gate — Self Review

- **Task / Issue**: #1398 D15 — context 生命周期全图化 + 通用落定 handler（框选手势/落定分层解耦）
- **Date**: 2026-09-05
- **Agent / Author**: pi (codex/1398-d15-lifecycle-graphs)

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（已有 op 的编排/连线，零新 graph 节点）

结论: PASS

一句话理由: 交付物是可组合的图体（box_hit/box_commit/box_hover_tick/box_hover_clear/selection_commit，全部用现有 op 连线）挂在新的 context 生命周期边界槽上（onActivated/onDeactivated——图引用槽，与 triggers[] 同性质，不是行为开关/继承 mode）。行为 100% 在图层里，引擎只负责在边界调用命名图体，不执行任何行为。op 面零新增：交接消费用集合减法语义（pending \ M = ∅，M⊆pending），不新增清除类 op。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 命中计算 | 2 | 图 `box_hit`（Query，复用） |
| 手势出口 | 2 | 图 `box_commit`（LoadEntryPayload/WriteBlackboardInt/InvokeGraph/WriteCollection/DeactivateContext） |
| 拖拽预览 | 2 | 图 `box_hover_tick`（action PointerMoved 输入边沿驱动） |
| 预览清场 | 2 | 图 `box_hover_clear`（Script 图体，WriteCollection replace ∅） |
| 通用落定 | 2 | 图 `selection_commit`（ReadBlackboardInt + QueryFromCollection + SwitchInt + WriteCollection；尾段用 Subtract 消费交接集 = ∅） |
| 候选集维护 | 2 | 图 `roster_sync`（复用，MapHeartbeat） |
| 边界图槽调度 | 0（机制） | 门控在"开启前/关闭后"执行命名图体；死亡走 destroy 边界同一槽 |

## 3. Reuse list

- Queues / Systems: `TriggerManager` owner 索引、`InteractionContextTriggerMountSystem`（门控挂/拆 + 槽执行）、`GraphReturnWriter`（图体执行，原 whileActive 同款调用签名）、`TriggerGraphActionBindingSystem`（输入边沿派发，新增 PointerMoved 变化边）、`EntityTriggerGraphMounts`（死亡路径基础设施）
- Registries: `InteractionContextProfileRegistry`（复用，扩展槽图解析）、`GraphProgramRegistry`
- Existing graphs: `box_hit`（命中纯函数，两处复用：hover/commit）
- Ops: 全部现有（Read/WriteBlackboardInt 304/301、QueryFromCollection 381、SwitchInt sugar、WriteCollection 477、InvokeGraph 450、DeactivateContext 475、ConstInt/Halt/LoadCaster）

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | — | 交接消费用集合减法（WriteCollection op=2，M⊆pending ⇒ pending\ M = ∅），不做新清除 op；该替代语义即为"消费交接"，非 hack。 |

## 5. Transaction boundary

无跨步骤 all-or-nothing 需求。Activate/Deactivate 沿用既有幂等失败（fail-fast）；落定为集合 replace/add/subtract，单步原子；死主体走统一心跳回收。context 生命周期槽图体失败与 triggers 图体同级 fail-fast，不做回滚。

## 6. Config SSOT

行为配置落在: graph assets（`assets/GAS/graphs/`）+ `interaction_context_profiles.json`（既有 schema 内字段集变更：删 `whileActive`，增 `onActivated`/`onDeactivated` 图引用数组）

是否新增 JSON schema: NO（新 schema 类型）——字段集变化作用于既有 interaction_context_profiles.json，行为全部在 graph 资产，槽字段与 `triggers[]` 同性质（图引用，非行为开关）。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（图槽引用未知图 / 非 TriggerGraph → fail-fast）
- [x] 未新增 graph op（入籍仪式成本高；现有 op 组合足够）

## 8. Next variant test

<<<<<<< HEAD
「下一个 Mod 变体」（手柄手势）将修改: **graph 连线 + profile 槽条目**（新 context + 新命中图 + `onDeactivated:[..., selection_commit]`），不改 Core enum、不改引擎、不改落定层。→ PASS

## 关联删除清单（成品自检用）

- `InteractionContextWhileActiveSystem.cs`（整个文件）
- `InteractionContextWhileActive` 类 / `WhileActive` 属性（Profile）、`ValidateWhileActive`（Loader）、`TryGetWhileActiveGraphId`/`ResolveWhileActiveGraphId`/`_whileActiveGraphIds`（Registry）
- GameEngine 对 WhileActiveSystem 的注册
- mod `interaction_context_profiles.json` 的 `whileActive` 字段
- `EntityCollectionRoleKind.AcquisitionPreview`：枚举保留（命令瞄准族其他消费者仍用），仅 whileActive 的该角色引用随系统删除

## 下层缺陷修复（D15 暴露，一并合入）

- `PlayerInputHandler.CompileBinding`：`Dictionary.TryGetValue` 失败时 out 参数写成 default(int)=0，未声明动作的绑定（引擎保留 id，如 PointerMoved）会别名到 index 0 动作槽并把指针值写进去。改为 TryGetValue 成功才赋值、失败编译为 -1 跳过（保留动作由引擎按 id 特判派发）。
- `WriteBlackboardInt` 描述符补 `scriptPorts`（对齐 WriteBlackboardFloat，TriggerGraph 可作者）并在 GraphKindOperationPolicy 加入 Script/TriggerGraph 的 GasTransactional carve-out（box_commit 在 rep 黑板交接修饰键位图）。
=======
「下一个 Mod 变体」将修改: graph 连线 / ParamBinding sourceId（黑板键名），不动 Core enum


## GAS Composition Gate — Self Review (#1404)

- **Task / Issue**: Mass Navigation 万人场景的出生效果请求超过固定队列容量
- **Date**: 2026-08-30
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（本任务不新增 effect 变体，只为既有出生 effect 组合补充场景容量声明）

结论: PASS

一句话理由: 复用现有 `onSpawnEffect`、`EffectRequestQueue` 和固定容量检查，只调整场景数据并补配置合同测试。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 单位出生时施加 `HealthDrift` | 2 | `Entities/templates.json` 的既有 `onSpawnEffect` 组合 |
| 出生效果请求固定容量 | 2 | `MassNavigationMod/assets/game.json` 的 `gasRuntimeCapacity` 场景声明 |
| 容量不足时显式失败 | 0/1 | 复用 `EffectRequestQueue.RequireAvailable` 与 `RuntimeEntitySpawnSystem` |

### 3. Reuse list

- Handlers: 既有 `RuntimeEntitySpawnSystem` 出生效果发布逻辑
- Queues / Systems: 既有 `EffectRequestQueue`、`ConfigPipeline`
- Resolvers / Registries: 既有配置合并和 `Entities/templates.json` 模板解析
- Existing presets / graphs: 既有 `Effect.MassNavigation.Agent.HealthDrift` 与 `Graph.MassNavigation.Agent.HealthDrift`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | N/A | 本任务不新增原子操作 |

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；只修改启动配置声明，不改变实体物化或 effect 事务。

### 6. Config SSOT

行为配置落在: `game.json`（`mods/capabilities/navigation/MassNavigationMod/assets/game.json`）的 `gasRuntimeCapacity.effectRequestQueueCapacity`；出生 effect 仍由 `Entities/templates.json` 声明。

是否新增 JSON schema: NO — 使用现有 `GasRuntimeCapacityConfig` 字段，不增加字段或加载器。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: effect 步骤（保持 `EffectRequestQueue` 固定容量合同不变）
>>>>>>> origin/main
