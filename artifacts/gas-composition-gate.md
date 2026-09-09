## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E 纠偏——退役档案空壳键；起角落操作者 rep；ScreenRect 按 audience；删四张 Score 适配器
- **Date**: 2026-09-03
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（放宽既有 op 图种白名单 + 既有 ParamBinding/档案字段可选 + 组合既有黑板/指针/audience 读面）

结论: PASS

一句话理由: 不新增 profile enum/平行管线；Write/ReadBlackboardFloat 扩到 TriggerGraph/Query；ParamBinding 补 ownerBlackboardFloat + pointerScreen；档案空壳键改为可选。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 档案 collection/view 键可选 | 0 小补丁 | InteractionContextProfileConfigLoader / Registry |
| 起角写/读 rep 黑板 | 0 白名单 + 2 图连线 | Write/ReadBlackboardFloat + box_begin/box_hit |
| ScreenRect 角点绑定 | 0 ParamBinding 源 | ownerBlackboardFloat / pointerScreen* |
| 框可见性 | 0 小补丁 | PresenterScreenRectSystem × PresenterRelationContext.Viewer × sole local viewer |
| 删 Score 适配器图 | 2 | Case E 资产 |

### 3. Reuse list

- Handlers: WriteBlackboardFloat / ReadBlackboardFloat / LoadPointerScreen*
- Systems: PresenterScreenRectSystem、InputContextProjectionSystem
- Resolvers: KnowledgeProjectionConsumer.TryResolveSoleLocalSeatViewer（仅作本机 audience 身份，不当迷雾）
- Registries: InteractionContextProfileRegistry、ConfigKeyRegistry（blackboardKey）
- Graphs: box_begin / box_hit / box_commit / selection_handle

### 4. New Layer 0 ops

N/A（不新增 opcode；只扩既有 op 的 authorableKinds）

### 5. Transaction boundary

无新事务壳；ActivateContext / DeactivateContext 既有生命周期不变

### 6. Config SSOT

- `interaction_context_profiles.json`（去掉空壳键）
- `Entities/templates.json`（BlackboardFloatBuffer）
- `GAS/graphs/graph.case_e.*`、`Presentation/presenters.json`
- 地图 Variables 去掉 press 角

是否新增 JSON schema: NO（档案字段改为可省略；ParamBinding 新增合法 source 字符串，非新 profile DSL）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加静默 fallback（缺黑板/缺指针 fail-fast；缺 Viewer 匹配则不画框）
- [x] 不用 Knowledge/Fog 管交互 UI 框

### 8. Next variant test

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

---

## GAS Composition Gate — DoOnce 图体流量控制糖（#1467）

- **Date**: 2026-09-07
- **Agent / Author**: ZCode (main session)

### 判断标准结论：通过

新变体是**编译期作者糖新增节点**（`GraphAuthoringSugar.DoOnce`），降级为既有 op 组合（ReadMapVarInt + ConstInt + CompareEqInt + JumpIfFalse + WriteMapVarInt + Jump），**零新 GraphNodeOp、零新 VM 指令、零 profile/enum 开关**——正是 composition gate 鼓励的方向。对照反例（被拒绝的形态）：在 TriggerGraph 入口上扩展 once 类声明式开关——入口 once 已存在（17 mod 在用，保留为关卡导演糖），本票补的是图体组合能力。

### 自审清单

1. 变体是 op 组合还是新开关？——组合（降级链七条指令全为既有 op）。
2. 状态存哪？——int map variable（作者命名 `var`），与 FsmState 的 stateVar 同一持久化机制；Reset ≡ WriteMapVarInt(var, 0)，不加 Reset 端口（零新端口机器）。
3. Void 节点的输出槽是共享 0 号寄存器——糖状态一律走 `AllocScratch` 专用寄存器（本票踩过这个坑后修正：state/const/bool 三格独立分配）。
4. 端口合同——true/false 双臂必填，对齐 BranchBool 校验；无值输入输出。
5. 图种门槛——Script/TriggerGraph，Query/Effect fail-closed（对齐 While/Until）。

### 复用/新增清单

| 类型 | 项 |
|------|-----|
| 复用 | GraphNodeOp 既有集、SugarScratch（扩展可选第三格）、EmitRelativeJump/ResolveControlTarget/RequireSymbol、FsmState 展开先例、BranchBool 端口合同 |
| 新增 Layer 0 | 无 |
| 新增 Layer 1 | 无 |
| 新增 Layer 2 | `GraphAuthoringSugar.DoOnce` 糖 + `GraphControlFlowCompiler.DoOnce.cs` 展开 + Bridge 投影一条 |
| 禁止项 | 无触碰（无 profile DSL、无平行加载器、无 enum 开关） |

### 验证

`GraphDoOnceSugarTests` 4/4（降级形态断言 + Query 拒绝 + 缺臂拒绝 + 缺 var 拒绝）；行为真机验证：`RegionVolumeTextbookAcceptanceTests`（教科书 ambush 图已改用糖——首过 true 臂刷怪、二过 false 臂计数）通过。
---

## GAS Composition Gate - MassNavigation Periodic Health Restoration

- Task / Date / Author: Restore the 10k showcase's authored health effect; 2026-09-09; Codex.
- Core judgment: A, PASS. Remove the showcase override so the existing spawn effect and graph are used.
- Layer assignment: Layer 2 entity template composition; existing Layer 0 attribute op and Layer 1 transaction are reused.
- Handlers: existing ModifyAttributeAdd graph operation.
- Queues / Systems: existing EffectRequestQueue, EffectLifetimeSystem, PresenterBehaviorSystem and HUD projection.
- Registries: existing EntityTemplateRegistry and EffectTemplateRegistry.
- Existing graph: Graph.MassNavigation.Agent.HealthDrift.
- New Layer 0 ops: N/A.
- Transaction boundary: existing effect phase transaction remains responsible for attribute changes and rollback.
- Config SSOT: mods/capabilities/navigation/MassNavigationMod/assets/Entities/templates.json and assets/GAS/effects.json, graphs.json.
- New JSON schema: NO.
- [x] No profile inherit/placement enum.
- [x] No parallel materialization pipeline.
- [x] No placement validation added to lifecycle operations.
- [x] No fallback or silent failure.
- Next variant: change effect steps or graph wiring in Mod assets.
- Validation: production-path test checks active effects, changing health, corresponding bars/numbers and retained HUD identities across two period windows.

## GAS Composition Gate — Effect Transaction Scale Regression

- **Task**: 修复持续效果事务在 10k 实体下的跨实体线性查找退化。
- **Date**: 2026-09-07
- **Core judgment**: PASS. 这是既有 GAS 生命周期和事务的实现修复，不新增 effect preset、profile enum、graph opcode 或平行管线。
- **Reuse**: `EffectLifetimeSystem`、`EffectPhaseSideEffectTransaction`、现有固定容量数组、TagOps、事件缓冲和生命周期 graph bindings。
- **Boundary**: 字典只做 Entity 到暂存数组行的定位；数组仍决定提交顺序、容量检查和回滚顺序。所有索引在 Begin/End 清空并复用。
- **No fallback**: 查找不到实体仍按既有“未暂存”路径处理；容量不足、缺组件和缺服务继续显式抛错。
- **Validation**: `EffectLifetimeScaleTests`、`EffectTransactionIndexTests` 和既有事务/挂接/分配回归测试；证据见 `docs/benchmarks/effect-transaction-pressure/`。
