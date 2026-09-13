## GAS Composition Gate — Self Review

- **Task / Issue**: MightyBubble/Ludots#1507（周期效果编译内核 + HUD 值绑定）
- **Date**: 2026-09-13
- **Agent / Author**: ZCode（分支 codex/gpu-skinned-commercial）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（对既有图程序做加载期静态判定与等价编译执行；HUD 值读取时机迁移；不新增 graph 节点类型、不新增 op）

结论: **PASS**

一句话理由: 交付物是"同一批既有 op 的等价快车道执行 + HUD 值的投影期现读"，组合面（effect template / graph / presenter 定义 JSON）零扩展，下一个 Mod 变体仍然只改连线与配置。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| OnPeriod 纯属性增量判定器（加载期静态证明） | 0（纯函数，职责单一：判定+编译） | `EffectPeriodKernel`（新，GAS 命名空间），由 `EffectExecutionPlanCompiler.FinalizeAll` 既有加载终点驱动 |
| 到期批执行（求值 + staging 直写） | 2 复用 1 | `EffectLifetimeSystem` PeriodGraphs 阶段逐条路由；写路径复用 `EffectPhaseSideEffectTransaction.StageAttributeAdd`（Layer 1 原子性壳，不新建事务） |
| HUD 值绑定声明 | 2（Mod 配置面不变：既有 attributeBinding + worldText 组合即触发） | presenter 定义编译期从同定义 `CompiledBindings` 解析属性源 |
| 屏幕值现读刷新 | 0 | `ScreenHudBatchBuffer.RefreshAttributeBoundTexts`（Hud 层纯数据刷新），`WorldHudToScreenSystem` 一行调用 |

### 3. Reuse list

- Handlers: `BuiltinHandlers.HandleApplyModifiers`（仅静态证明"空修饰符 → no-op"，不复制实现）
- Queues / Systems: `EffectDueWheel`（due 出桶，不改）、`EffectLifetimeSystem`（PeriodGraphs 阶段）、`AttributeAggregatorSystem`（消费侧不改）、`ClearPresentationFlagsSystem`（不改）
- Resolvers / Registries: `EffectTemplateRegistry`（内核表宿主，与 execution plans 同生命周期）、`GraphProgramRegistry`、`PresetTypeRegistry`、`BuiltinHandlerRegistry`、`GlobalPhaseListenerRegistry`（经 `EffectPhaseExecutor` 只读查询）
- 事务: `EffectPhaseSideEffectTransaction.StageAttributeAdd` / `Commit` / `Rollback`（零新增事务 API）
- RNG: `EffectLifetimeSystem.BuildExecutionSeed` + `EffectPhaseExecutor.BuildRandomSeed`（改 internal static 复用，保证与解释 VM 逐位同种子）
- HUD: `WorldHudBatchBuffer.TryAdd`（serial 稳定性天然去重）、`ScreenHudBatchBuffer` 脏文本增量、`PresentationOverlaySceneBuilder` 数值格式化车道、`HudItemIdentity` serial 组合

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `EffectPeriodKernel`（判定器+求值器） | 加载期把已验证指令流编译为稠密内核程序并在 due 时求值 | 它是图 VM 的等价快车道，不是新 op；组合既有 op 恰是被替代对象（每事件 ~15µs 解释税） |
| `ScreenHudBatchBuffer.RefreshAttributeBoundTexts` | 屏幕文本 HUD 的绑定值现读刷新 | 投影层新数据源（AttributeBuffer 现读），无既有等价物 |

### 5. Transaction boundary

批执行原子性沿用现相位事务：内核条目经 `StageAttributeAdd` 进同一 `EffectPhaseSideEffectTransaction`，slice 中止 → 既有 `Rollback` 整体恢复；提交 → 既有单次 `Commit` flush（批量脏标/聚合/表现位均在既有提交循环内，同批去重由 `AttributeAggregateDirtyRegistry` 与 `DirtyEntityQueue` 内建去重承担）。

守卫取舍：路由期预检（目标存活 / `AttributeBuffer` / `DirtyFlags` 齐备 + 无 Phase Listener 干涉），预检不过的条目落回逐事件解释路径重放——解释路径对同一失败面抛出与改造前完全一致的错误；预检通过后批内无剩余失败面（单线程 slice 内世界状态不变），无需批中途回滚。

### 6. Config SSOT

行为配置落在: `mods/**/assets/GAS/effects.json` + `graphs.json`；HUD 绑定声明落在 `mods/**/assets/Presentation/presenters.json`（既有 worldText + attributeBinding 组合，无新字段）。

是否新增 JSON schema: **NO**

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback——不可证明纯增量的 OnPeriod 留解释 VM 是双车道健全性边界（合同写在 `EffectPeriodKernel` 类型注释），不是 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / effect 步骤**（例如漂移上限加 ClampFloat 节点、再加纯算术节点——判定器白名单自动覆盖；超出白名单自动留解释 VM，无需 Core enum 变更）。
