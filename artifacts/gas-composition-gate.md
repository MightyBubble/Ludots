# GAS Composition Gate — Self Review

- **Task / Issue**: #1398 D15 — context 生命周期全图化 + 通用落定 handler（框选手势/落定分层解耦）
- **Date**: 2026-09-05
- **Agent / Author**: pi (codex/1398-d15-lifecycle-graphs)

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（已有 op 的编排/连线为主）+ 一个单一职责 Layer 0 op

结论: PASS

一句话理由: 交付物是可组合的图体（box_hit/box_commit/box_hover_tick/box_hover_clear/selection_commit，全部用现有 op 连线）挂在新的 context 生命周期边界槽上；新增的唯一 op `ResetTargetList` 是"TargetList:=∅"的单一职责原子操作，无法由现有 op 组合（唯一替代是 InvokeGraph 一个空子图的 hack，否决）。onActivated/onDeactivated 是**图引用槽**（与 triggers[] 同性质），不是行为开关/继承 mode——行为 100% 在图层里，引擎只负责在边界调用命名图体，不执行任何行为。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 命中计算 | 2 | 图 `box_hit`（Query，复用） |
| 手势出口 | 2 | 图 `box_commit`（WriteBlackboardInt/InvokeGraph/WriteCollection/DeactivateContext） |
| 拖拽预览 | 2 | 图 `box_hover_tick`（PointerMoved 输入边沿驱动） |
| 预览清场 | 2 | 图 `box_hover_clear`（WriteCollection replace ∅） |
| 通用落定 | 2 | 图 `selection_commit`（ReadBlackboardInt + QueryFromCollection + SwitchInt + WriteCollection + ResetTargetList） |
| TargetList 置空 | 0 | 新 op `ResetTargetList`（单一职责、不可由现有 op 组合） |
| 候选集维护 | 2 | 图 `roster_sync`（复用，MapHeartbeat） |
| 边界图槽调度 | 0（机制） | 门控（InteractionContextTriggerMountSystem）在"开启前/关闭后"执行命名图体；死亡走同一槽 |

## 3. Reuse list

- Queues / Systems: `TriggerManager` owner 索引、`InteractionContextTriggerMountSystem`（门控挂/拆）、`GraphReturnWriter`（图体执行，原 whileActive 同款调用签名）、`TriggerGraphActionBindingSystem`（输入边沿派发）、`EntityTriggerGraphMounts`（死亡路径基础设施）
- Registries: `InteractionContextProfileRegistry` / `ProfileIdRegistry`（复用，扩展字段解析）、`GraphProgramRegistry`
- Existing graphs: `box_hit`（命中纯函数，两处复用：hover/commit）

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `ResetTargetList` | TargetList := ∅ | 现有 op 只能"填"TargetList（各 Query/InvokeGraph），无"清"的能力；等价唯一替代是 InvokeGraph 一个空查询图（hitCount=0 拷贝回的副作用 hack），引入隐藏图资产且语义不明。 |

## 5. Transaction boundary

无跨步骤 all-or-nothing 需求。Activate/Deactivate 沿用既有幂等失败（fail-fast）；落定为集合 replace/add/subtract，单步原子；死主体走统一心跳回收。context 生命周期槽图体失败与 triggers 图体同级 fail-fast，不做回滚。

## 6. Config SSOT

行为配置落在: graph assets（`assets/GAS/graphs/`）+ `interaction_context_profiles.json`（既有 schema 内字段集变更：删 `whileActive`，增 `onActivated`/`onDeactivated` 图引用数组）

是否新增 JSON schema: NO（新 schema 类型）——字段集变化作用于既有 interaction_context_profiles.json，行为全部在 graph 资产，槽字段与 `triggers[]` 同性质（图引用，非行为开关）。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（图槽引用未知图 / 非 TriggerGraph → fail-fast，同 whileActive 语义）

## 8. Next variant test

「下一个 Mod 变体」（手柄手势）将修改: **graph 连线 + profile 槽条目**（新 context + 新命中图 + `onDeactivated:[..., selection_commit]`），不改 Core enum、不改引擎、不改落定层。→ PASS

## 关联删除清单（成品自检用）

- `InteractionContextWhileActiveSystem.cs`（整个文件）
- `InteractionContextWhileActive` 类 / `WhileActive` 属性（Profile）、`ValidateWhileActive`（Loader）、`TryGetWhileActiveGraphId`/`ResolveWhileActiveGraphId`/`_whileActiveGraphIds`（Registry）
- GameEngine 对 WhileActiveSystem 的注册
- `EntityCollectionRoleKind.AcquisitionPreview`（仅 WhileActiveSystem 引用）
- mod `interaction_context_profiles.json` 的 `whileActive` 字段
