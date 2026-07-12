## GAS Composition Gate — Self Review

- **Task / Issue**: #650, #649, #651
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（沿现有 Order/Input 管线扩展类型化结果合同；不新增 gameplay 变体）

结论: PASS

一句话理由: 修改限于现有订单接入、订单终态和输入激活入口，不新增 profile、preset、graph、lifecycle op 或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 订单接入结果 | N/A | 现有 OrderQueue、OrderSubmitter、OrderBufferSystem |
| 订单唯一终态 | N/A | 现有 OrderSubmitter、AbilityExecSystem、OrderContinuationSystem |
| 角色隔离激活 | N/A | 现有 InputOrderMappingSystem、EntityCommandPanelMod |

### 3. Reuse list

- Handlers: 现有 InputOrderMappingSystem.OrderSubmitHandler
- Queues / Systems: OrderQueue、OrderBufferSystem、AbilityExecSystem、OrderContinuationSystem
- Resolvers / Registries: OrderTypeRegistry、AbilityDefinitionRegistry、现有 actor/mapping 解析
- Existing presets / graphs: N/A

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；订单 finalize 通过单一入口保证每个 active order 只结束一次。

### 6. Config SSOT

行为配置落在: 现有 order type catalog 与 OrderBuffer 正式容量。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: effect 步骤（本任务本身不引入 Mod gameplay 变体）

---

## GAS Composition Gate — #646 Self Review

- **Task / Issue**: #646
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：沿现有 Effect Phase、BuiltinHandler、EffectRequestQueue 与时间切片合同重组执行路径，不新增 profile 字段、preset 枚举、Graph VM 或平行管线。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 瞬时效果阶段组合 | 2 | 现有 `EffectPhaseExecutor` + effect template phase bindings |
| 预设主行为 | 0 | 现有 `BuiltinHandlerRegistry` / `BuiltinHandlers` |
| 生命周期切片 | N/A | 现有 `EffectLifetimeSystem` + `ITimeSlicedSystem` |
| 后续效果发布 | N/A | 现有 `EffectRequestQueue` |

### 3. Reuse list

- Handlers: `BuiltinHandlerRegistry`, `BuiltinHandlers`, `BuiltinHandlerExecutionContext`
- Queues / Systems: `EffectRequestQueue`, `EffectProcessingLoopSystem`, `EffectLifetimeSystem`, `AbilityExecSystem`
- Resolvers / Registries: `EffectPhaseExecutor`, `EffectTemplateRegistry`, existing target resolvers and graph program registry
- Existing presets / graphs: existing `EffectPresetType` definitions and phase graph bindings; no new variant

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

瞬时效果必须在同一次正式阶段执行中完成 OnResolve、OnHit、OnApply，并在结束时清理配置上下文和扇出暂存；需要跨帧监听器所有权的效果不得进入瞬时路径。

### 6. Config SSOT

行为仍由现有 effect template、preset catalog 和 graph 配置表达。新增的仅是 `game.json` 中启动期 GAS 快照容量；没有新增玩法 DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建平行瞬时运行时、Graph VM 或 loader
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 容量不足明确失败，预算不足明确延后，无 fallback/静默截断

### 8. Next variant test

下一个 Mod 变体只调整 graph 连线或 effect 步骤，不修改 Core enum。

---

## GAS Composition Gate — #647 Self Review

- **Task / Issue**: #647
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：统一现有生产 Graph API 的服务装配和生命周期/诊断接线；没有新增 graph op、preset 字段、玩法枚举或平行运行时。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 生产图服务装配 | N/A | `GasGraphRuntimeApi.CreateProduction` + `GasGraphRuntimeProductionServices` |
| 派生属性图执行 | 2 | 现有 `GraphProgramRegistry` + `AttributeAggregatorSystem` |
| 输出生命周期 | N/A | 现有 `GraphOutputValueStore` + Cleanup system |
| GAS 告警出口 | N/A | 现有 `GasBudget` / `OrderAdmissionResultBuffer` + 固定容量结构化事件缓冲 |

### 3. Reuse list

- Handlers: existing `GasGraphOpHandlerTable`
- Queues / Systems: `AttributeAggregatorSystem`, `GasBudgetReportSystem`, `OrderAdmissionResultBuffer`
- Resolvers / Registries: `GraphProgramRegistry`, topology services, `GraphOutputValueStore`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

生产 Graph API 构造要求完整服务集合，缺失任一正式依赖立即失败；owner 版本退役时，输出槽位、哈希索引和旧句柄在同一 Cleanup 更新中一起失效。

### 6. Config SSOT

没有新增玩法配置 schema。生产服务来自引擎唯一强类型服务集合；诊断指标来自 `GasBudget` 与订单接入结果。

### 7. Red flag scan

- [x] 未新增 profile enum 或 graph op
- [x] 未建立第二套 Graph API 运行时
- [x] 未用全 ECS 表扫描或定期全清实现输出回收
- [x] 缺服务、诊断缓冲溢出和计数器回退均 hard-stop

### 8. Next variant test

下一个 Mod 变体继续通过现有 graph 连线和服务集合接入，不修改 Core enum。

---

## GAS Composition Gate — #653 Self Review

- **Task / Issue**: #653
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：收口现有有效技能槽位解析与输入/展示接线；没有新增 ability profile、preset、graph op 或第二套优先级规则。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 有效技能槽位解析 | N/A | 唯一 `AbilitySlotResolver.Resolve` |
| 输入覆盖 | N/A | `SkillMappingOverrideResolver` |
| 面板与实体信息展示 | N/A | 现有 EntityCommandPanel / EntityInfo 消费者 |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing input mapping, routing, aiming and execution systems
- Resolvers / Registries: `AbilitySlotResolver`, `AbilityDefinitionRegistry`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

同一 actor/slot 的 base、form、item、granted 来源必须一起参与一次确定性解析；不完整重载被删除，生产调用无法再省略 item 层。

### 6. Config SSOT

没有新增配置 schema。优先级 SSOT 固定为 `granted > item > form > base`，输入覆盖继续来自有效 `AbilityDefinition.InputBindingOverride`。

### 7. Red flag scan

- [x] 未新增 profile enum 或输入 fallback
- [x] 未在消费者复制第二套优先级梯子
- [x] 不完整 resolver 重载已删除
- [x] 预热后的输入覆盖解析 0 分配

### 8. Next variant test

新增技能来源必须扩展唯一 resolver 合同和全链路一致性测试，不得只改单个消费者。
