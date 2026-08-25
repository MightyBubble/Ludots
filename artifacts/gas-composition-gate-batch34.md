# GAS Composition Gate — 第三批（#1123 / #1124）+ 第四批（#1125 / #1126）

- **Date**: 2026-08-24
- **Agent / Author**: ZCode (GLM-5.3) on codex/night-raid-circle-visual-fix

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**（#1123 = 派发表扩展；#1124 = entry priority 字段 + 注册期拼接 post-pass；#1125 = 编译期糖零新执行器；#1126 = 一个 Yield 类原子 op + 恢复队列系统）

一句话理由: 无新 profile DSL、无平行物化管线、无 enum 开关；全部落在 atomic op / 既有注册表 / 编译期展开。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---|---|
| #1123 global 表 + FireGlobalEvent/FireCrossMapEvent | 0 | TriggerManager（照 _mapEventTriggers 注册期排序模式） |
| #1123 SourceMapId payload 键 | 0 | MapTriggerEventPayloadKeys + DynamicBridge 账面 |
| #1123 DispatchMapEvent scope 参数扩展 | 0 | #1115 op 的 scope 字段（global/map:id） |
| #1124 entry priority:int | 0 | TriggerGraphEntry + AddMapEventTrigger 既有排序（零运行时新代码） |
| #1124 hookAnchor/hookNode | 0→2 | TriggerGraphHookWeaver（注册后 post-pass，ReplaceProgram 落地） |
| #1125 EnumCatalog + SwitchOnEnum/SelectByEnum | 0(数据)+糖 | EnumCatalogLoader（仿 CustomEventCatalog）；case 端口解析 + CompareEqInt/JumpIfFalse/MoveInt 展开 |
| #1126 AwaitCallback op | 0 | HandleAwaitCallback（注册句柄 + Status=Yielded） |
| #1126 CallbackDispatcher + Continuation Phase | 0 | 新 SystemGroup 成员 + ITimeSlicedSystem 预算 |

## 3. Reuse list

- Handlers: HandleYield、HandleInvokeScript 子帧、TriggerGraphMountTrigger park/resume（ResumeFromSuspension）
- Queues / Systems: PhaseOrderedCooperativeSimulation.UpdateSlice 预算机制、MapHeartbeatClockSystem 时钟先例、OrderContinuationSystem 队列先例
- Resolvers / Registries: GraphProgramRegistry.ReplaceProgram（拼接落点）、EnsureNoInvokeCycle（hook 环检测复用）、DetectUnreachable（孤立节点拒绝）、EventSchemaRegistry（callback schema 模式）、ConfigMerger.ArrayAppendFields（enums members 追加语义）
- Existing presets / graphs: 夜袭图（#1125 FSM showcase 主循环载体）

## 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|---|---|---|
| #1126 AwaitCallback=454 | 注册具名回调句柄并挂起当前 run | 注册必须发生在运行时（句柄生成+payload 携带），纯编译期无法表达 |
| （#1123/#1124/#1125 零新 op） | — | 全部复用 #1115 DispatchMapEvent、既有排序/拼接/糖机制 |

## 5. Transaction boundary

必须原子 rollback 的步骤: 无（挂起-恢复由句柄失效校验 fail closed 保证，无需回滚）。

## 6. Config SSOT

行为配置落在: Enums/enums.json（#1125，ArrayById+ArrayAppendFields:members）、Callbacks/callbacks.json（#1126，同管线）、graphs.json entries（#1124 priority/hook 字段）。

是否新增 JSON schema: YES（两个新 catalog 文件，但零新 loader 管线——全部走 ConfigPipeline 既有模式）。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（suspended map 不收 global = 拍板的显式语义；targetMap 不存在 fail closed = 显式）

## 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / enums.json 数据（零 Core enum 改动）
