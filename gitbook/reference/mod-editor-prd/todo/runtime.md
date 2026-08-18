# TODO · 运行时横切与编辑器卷

> 卷 10-11（rt-01…rt-05、ed-01…ed-03）写作与审计沉淀的治理项，编号 R 系列；跨域总账见 [backlog](backlog.md)。spec 层以编号引用本表。严重度：高（误导用户/数据错误）· 中（易用性/体系缺口）· 低（打磨）。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| R1 | 中 | 两个"256 级"数字职责不同易混：单根创建上限（MAX_CREATES_PER_ROOT，引擎常量）与单根记账表容量（=effectFanOutCommandCapacity，game.json）——工具与文档易当同一上限混配混读 | src/Core/Gameplay/GAS/GasConstants.cs:16；src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs:73；RootBudgetTable.cs:123-155 拒绝路径不注明是哪一种 | 错误信息与注释双处显式区分"per-root creates cap"与"table capacity"；编辑器预算监视器分区渲染（rt-02 spec 任务） | 待立项 |
| R2 | 中 | 错误码无集中字典：32 族 165 个 GAS.*.ERR.* 字面量散布约 40 文件（最大族 GAS.GRAPH.ERR×43，GasGraphRuntimeApi.cs 单文件 25 处）——码即合同却无单一事实源，检索与文档化靠全文扫描 | 全 src 扫描 `GAS.*.ERR.*`；典型 src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs | 建错误码注册表（常量类或生成清单），抛出点引用注册表；字面量格式不变仅收敛定义位置；编辑器字典同源消费（rt-03 spec 任务） | 待立项 |
| R3 | 中 | 遥测溢出语义与全链 fail-fast 不一致：ResponseChainTelemetryBuffer 溢出静默丢弃+计数，而表现事件/诊断缓冲/事件总线同为观测通道却满抛——同为"遥测"行为分两派，工具侧无法统一处理 | src/Core/Gameplay/GAS/ResponseChainTelemetryBuffer.cs:51-60（对照 GasPresentationEventBuffer.cs:81-89、GasDiagnosticEvents.cs:97-105、GameplayEventBus.cs:62-76） | 裁决统一（fail-fast）或明文化"观测面允许有界丢弃+显式计数"合同；裁决前 UI 分色呈现（rt-05 spec 任务） | 待立项 |
| R4 | 低 | 热字段白名单注释漂移：TryReplaceHotNumericField 的 XML 注释只列 duration 两路径，实现实际支持 modifiers.0.value——白名单注释即合同，漏项误导调用方 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:476（对照 :493-548 三分支） | 注释补全三路径；后续白名单注释与实现同步纳入评审项（ed-02 spec 任务） | 待立项 |
| R5 | 高 | 工作台文档投影源缺生产实现：ILiveSkillWorkbenchDocumentSource 唯一实现是测试桩，ModEntry 启动即无文档——UI 目录树默认空，编辑器主界面无供血 | src/Tests/WebUiDataPlaneTests/LiveSkillWorkbenchDataPlaneTests.cs:641；LiveSkillWorkbenchModEntry.cs:105-112 可选注入；Runtime LoadFromSource:384 | 生产投影源从"配置文件+运行注册表"合成目录树文档（同源合成不做第二事实源），随快照发布（ed-03 spec 任务，路线图 #1） | 待立项 |
| R6 | 中 | AgentBridge（#1001）17 个工具与队列/泵语义零自动化测试：验收全靠 pi/deepseek 会话；SyntheticInputDevice 是纯类（帧边沿/按下保持/ReleaseAll）极易单测，回归防线缺失 | 全 src/Tests 无 AgentBridge 引用；被测面 src/Libraries/Ludots.AgentBridge/*.cs、src/Core/Input/Runtime/SyntheticInputDevice.cs | 新增 AgentBridgeTests：SyntheticInputDevice 帧语义、Pump 预算与超时、JSON-RPC 错误码映射、InputTools 参数校验 | 待立项 |
| R7 | 低 | SolePlayerId 无座位时魔法回退 1：调试便利压过 fail-fast 纪律，座位模型变更后静默指向错误玩家 | src/Libraries/Ludots.AgentBridge/AgentToolContext.cs:20-25 | 无座位时抛 service.unavailable 指引先建立座位，或回退值显式入文档 | 待立项 |
