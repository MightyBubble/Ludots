# GAS Composition Gate — MapTriggerGraph MVP（#1030 首批切片 + 火球 showcase 迁移）

## GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1030（MapTriggerGraph）首批切片：L1 方言 + 挂载宿主 + 引擎接线；火球四皮 showcase 由 C# 触发器迁至 MapTriggerGraph
- **Date**: 2026-08-20
- **Agent / Author**: ZCode session（orchestrator + 2 implementation subagents）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: B——新图方言与数据挂载（不是新 op、不是 preset 开关、不是 profile enum）

结论: PASS

一句话理由: 「地图事件→反应」的新表达落在 GraphKind.MapTrigger 作者面 + MapConfig.MapTriggerGraphs 数据挂载上，运行时完全复用既有 VM/登记表/TriggerManager 管线，零新 opcode、零平行执行器、零平行事件总线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| MapTrigger 作者方言（entries[] → StartPc） | L1 | GraphControlFlowDocument/Compiler + GraphProgramAuthoringFrontDoor |
| 登记表携带入口表 | L0 | GraphProgramRegistry/GraphProgramPackage + MapTriggerGraphEntry |
| 挂载解析与校验（fail-closed） | L2 挂载面 | src/Core/Gameplay/MapTriggers/MapTriggerGraphMount(ing) |
| 事件分发 | 既有 TriggerManager | MapTriggerGraphMountTrigger : Trigger（每 mount×entry 一实例，走 RegisterMapTriggers/装饰器/卸载回收既有路径） |
| 切片执行 | L0 既有 | GraphExecutor.ExecuteResolvedRegisteredScriptSlice（caller-owned registers，起 PC=StartPc） |
| 动作宿主声明 | 既有 | GraphActionHost.MapTrigger = 5（MVP 不入 AllowsYield） |

### 3. Reuse list

- Handlers: 既有 CreatePanel/ShowPanel 等 op handler 原样复用，未新增
- Queues / Systems: TriggerManager 事件路由、RegisterMapTriggers/UnregisterMapTriggers 生命周期、TriggerDecoratorRegistry.Apply 装饰器路径全部复用
- Resolvers / Registries: GraphProgramRegistry、GraphIdRegistry（name→id，非 obsolete 路径）、MapSession.EntityIndex（scope 解析）、GraphActionHost/GraphVmLimits/GraphExecutionCursor
- Existing presets / graphs: 火球 showcase 的 Graph.Fireball.Panel.OpenStatus 节点体不变（LoadExplicitTarget→CreatePanel→ConstInt→HaltReturnInt），仅 kind/入口声明由 Script+entry 换为 MapTrigger+entries

### 4. New Layer 0 ops (if any)

N/A——零新 opcode。入口分发是把 cursor 起始 PC 设为 entry.StartPc，作者语义不占用新指令。

### 5. Transaction boundary

必须原子 rollback 的步骤: GraphProgramRegistry.Register/ReplaceProgram 携带入口表校验（重复 label/越界 StartPc/非 MapTrigger 携表）失败时回滚保留原登记（既有 rollback 路径扩展，未新开事务面）。

### 6. Config SSOT

行为配置落在: graph（GAS/graphs.json，kind:"MapTrigger" + entries[]）+ map（MapConfig.MapTriggerGraphs: [{graph, scopeInstanceId}]）

是否新增 JSON schema: YES——MapTrigger 图 entries[] 与地图挂载对象两处新作者字段；不通过组合现有字段表达是因为「事件入口表」与「图挂载声明」是本切片交付的本体概念，且均为严格加载（未知字段拒绝、必填校验、fail-closed 报错指名图/地图/字段）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（挂载触发器只是 Trigger 子类，走既有注册路径）
- [x] 未把 placement 校验塞进 lifecycle op（挂载校验在 mount 解析期，不在任何 op 内）
- [x] 未添加「说不清的」默认 fallback（once 默认 false、scopeInstanceId 缺省=Entity.Null，均为显式成文语义；其余缺省全部抛错）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（新入口/新反应节点）或 map 挂载声明（新图/新 scope）——只动数据，不动 Core enum。下一个事件族（RegionEntered/EntityDied 等词典切片）同样只加 EventKey 供给与图 entries 引用，不动方言结构。

### 附：本切片边界（成文的 MVP 限制）

- MapTrigger 作者策略镜像 Script 减去 Yield/Wait（挂载宿主尚无跨拍续跑；时间线随 ThinkWaveElapsed 切片开放）
- 事件入口仅支持既有 EventKey 词表（MapLoaded/MapUnloaded 等）；builtin 事件词典、mod 域挂载、叠加仲裁、override 属 #1030 后续切片
- GraphActionHost.Level 未在本切片退役（存量 LevelDirector 试验品仍引用，归 #1030 硬编码清算切片）
