# GAS Composition Gate — #1116 InvokeGraph 子图复用 + #1115 DispatchMapEvent 结构化 payload

- **Task / Issue**: #1116 (InvokeGraph 子图复用 + StoreArg 暂存) / #1115 (DispatchMapEvent 结构化 payload)
- **Date**: 2026-08-24
- **Agent / Author**: ZCode (graph-editor-audit worktree)

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: A —— 5 个新 graph 节点（InvokeGraph / StoreArgInt / StoreArgFloat / StoreArgEntity / DispatchMapEvent）及其连线组合语义

结论: PASS

一句话理由: 需求全部由原子 op 组合表达（StoreArg* 暂存 + InvokeGraph 子帧 + DispatchMapEvent fire），零新 profile enum、零新 preset 开关、零平行管线。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| StoreArgInt/Float/Entity | 0 | GasGraphOpHandlerTable 原子 handler（写 per-run 暂存表） |
| InvokeGraph | 0 | GasGraphOpHandlerTable 原子 handler（复刻 InvokeScript 子帧模式） |
| DispatchMapEvent | 0 | GasGraphOpHandlerTable 原子 handler → IGraphRuntimeApi fire 桥 |
| 子图复用（夜袭重构） | 2 | mods/.../graphs.json 连线改动 |
| 事件结构化校验 | 0（编译期）+ 现有 EventSchemaRegistry/ValidateFirePayload | GraphControlFlowCompiler + 既有 fire 期兜底 |

## 3. Reuse list

- Handlers: HandleInvokeScript 子帧模式（stackalloc 寄存器、InvokeDepth、共享 World/Api/Programs/TreeSteps/DebugTrace）
- Queues / Systems: TriggerManager.FireMapEvent（map 域派发）、TriggerGraphMountTrigger 挂载/resume 管线、EventDispatch phase 不动
- Resolvers / Registries: GraphProgramRegistry（RequireKind/ValidateProgramInvokeTargets/EnsureNoInvokeCycle）、EventSchemaRegistry（TryGet/ValidateFirePayload）、ConfigKeyRegistry（arg key 符号）、GraphIdRegistry、CustomEventCatalog
- Existing presets / graphs: 夜袭 Graph.NightRaid.Flow（重构为调用 WriteStage/SpawnRaiderRow/SpawnEliteRow 子图）；LoadEntryPayload*（413-415）作为子图读参的零新读取 op
- Existing payload table: GraphEntryPayloadTable 直接复用为暂存载体（新增 upsert 语义，entry capture 语义不变）

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| InvokeGraph | 同步调用另一 TriggerGraph 至 halt，回传 HaltReturnInt | InvokeScript 只接受 Script kind 且无 entry/传参契约 |
| StoreArgInt/Float/Entity | 把一个寄存器值按 key 写入 per-run 暂存 | 现有 op 无任何暂存写语义；blackboard/mapvar 是跨 run 持久域，语义不同 |
| DispatchMapEvent | 按 schema 把暂存组装成 ScriptContext 并 map 域 fire | FireEventKey 无 payload 契约；SendEvent 是 GAS tag 事件，非 TriggerManager 通道 |

## 5. Transaction boundary

无 GAS 事务步骤（TriggerGraph 域全部为即时 op，非 effect transactional）；InvokeGraph 子帧共享 TreeSteps 预算，超限整体抛错（all-or-nothing 由 VM 预算保证）。

## 6. Config SSOT

行为配置落在: mods 的 graphs.json（TriggerGraph 连线）+ Events/custom_events.json（事件 schema SSOT，扩展自 event parameter schema SSOT）

是否新增 JSON schema: NO —— 复用 GraphControlFlowDocument 节点字段与 custom_events.json 既有 schema。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未传 arg 读时 EntryPayloadKeyNotCarried / 未注册事件编译期+运行期双拦 / 循环调用 EnsureNoInvokeCycle 点名）

## 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（新增第 7 个 write_stage 调用点 = ConstInt+StoreArgInt+InvokeGraph 三连线，子图零改动）。
