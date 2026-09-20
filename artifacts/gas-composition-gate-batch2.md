# GAS Composition Gate — 第二批（#1113 / #1115 / #1116 / #1108）

- **Task / Issue**: #1113 MapVariableChanged / #1115 DispatchMapEvent / #1116 InvokeGraph / #1108 LoadPlacedEntity
- **Date**: 2026-08-24
- **Agent / Author**: ZCode (GLM-5.3) on codex/night-raid-circle-visual-fix @ 3dab584484+

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 节点 / op / 已有 op 的连线与参数；#1113 是引擎事件 + 既有 fire 管线的重写）

结论: **PASS**

一句话理由: 全部交付物为 atomic graph ops（InvokeGraph/StoreArg*/DispatchMapEvent/LoadPlacedEntity）+ 既有事件管线的通用化（MapVariableChanged），无新 profile enum、无平行物化管线。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|---|---|---|
| InvokeGraph (449) 子图调用 | 0 | GasGraphOpHandlerTable.HandleInvokeGraph（复刻 HandleInvokeScript 子帧） |
| StoreArgInt/Float/Entity (450-452) | 0 | handler 写 GraphEntryPayloadTable 暂存（复用 #1106 表） |
| DispatchMapEvent (453) | 0 | handler 组装 context → TriggerManager.FireMapEvent（复用既有派发口） |
| LoadPlacedEntity (416) | 0 | handler 查 MapLoadEntityIndex（复用 #1106 同一索引正查） |
| MapVariableChanged | 0 | MapVariableStore.WriteInt/WriteFloat → 引擎 dispatcher → FireMapEventAsync |
| varName entry filter | 0 | TriggerGraphEntryFiltersEvaluator（仿 Action 块） |
| 夜袭 write_stage 子图化 | 2 | graphs.json 数据改动（Mod 可改层） |

## 3. Reuse list

- Handlers: HandleInvokeScript（子帧隔离模式）、HandleLoadEntryPayload*（413-415 读取侧零新增）、RequireEntryPayloadKey 模式
- Queues / Systems: TriggerManager.FireMapEvent/Async（派发口）、MapHeartbeatClockSystem think wave（挂起恢复）、EventSchemaRegistry.ValidateFirePayload（fire 期校验免费）
- Resolvers / Registries: GraphProgramRegistry（kind 校验 + EnsureNoInvokeCycle 循环检测并入）、ConfigKeyRegistry/Register 符号 intern、MapLoadEntityIndex（#1108 正查 / #1106 反查同源）、EventSchemaRegistry（端口/schema SSOT）
- Existing presets / graphs: 夜袭 Flow 图 write_stage 家族与 spawn 行（重构目标）

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|---|---|---|
| InvokeGraph=449 | 调用 TriggerGraph 子图指定 entry 并等 ReturnInt | InvokeScript 强制 RequireKind(Script)，解禁=跨 kind 语义不明（禁跨职责） |
| StoreArgInt/Float/Entity=450-452 | 把寄存器值写入调用暂存表具名槽 | 单指令仅 3 源寄存器，>3 参无法编码；现有 op 无暂存写侧 |
| DispatchMapEvent=453 | 按事件名+schema payload 派发 map 事件 | FireEventKey 无图接线、无 payload 端口、无 scope；扩展它=匿名 payload 违 #1114 |
| LoadPlacedEntity=416 | 按 InstanceId 读放置实体（fail-closed Null） | 现无任何 op 消费 MapLoadEntityIndex 正查 |

## 5. Transaction boundary

必须原子 rollback 的步骤: 无（全部为读/派发原子 op；InvokeGraph 子帧 stackalloc 天然回滚即丢弃）。

## 6. Config SSOT

行为配置落在: effect template / graph / catalog（路径）: graphs.json（图结构）、Events/custom_events.json（事件 schema）、Enums/enums.json（#1125 后续）

是否新增 JSON schema: YES（无新 loader —— 全部走 ConfigPipeline ArrayById 既有管线与 CustomEventCatalog 模式）

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（LoadPlacedEntity 的 Entity.Null 是 issue 明确的 fail-closed 合同，非 fallback）

## 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（新变体=新子图+新调用点连线；零 Core enum 改动）
