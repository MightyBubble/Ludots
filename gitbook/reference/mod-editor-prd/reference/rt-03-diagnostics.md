# rt-03 reference · 诊断与错误码

> 现状参考。第一性需求见 [rt-03 PRD](../prd/rt-03-diagnostics.md)；配置说明见 [rt-03 配置说明](../config/rt-03-diagnostics.md)。

## 1. 现状快照

- System 枚举九值：ResponseChain/EffectProposal/EffectLifetime/TagContainer/ActiveEffectContainer/PhaseListener/GameplayEventBus/OrderAdmission/EffectApplication。
- Metric 实为 20 个命名值，编号 1-22 且 5、6 缺号：1-4 响应链丢弃类；7-11 容器/监听/事件总线丢弃；12-22 订单准入溢出与八种拒绝 + backlog/high-watermark。
- 事件结构 GasDiagnosticEvent(FrameIndex, System, Metric, Capacity, Count)；缓冲默认容量 32、帧首清零、Publish 满抛（诊断通道自身 fail-fast）。
- 唯一发布方：GasBudgetReportSystem（EventDispatch 组，每帧把预算与订单准入非零指标发布进缓冲）。
- 错误码现状：无集中字典——32 族 165 个 `GAS.*.ERR.*` 字面量散布约 40 文件，最大族 GAS.GRAPH.ERR×43（GasGraphRuntimeApi.cs 单文件 25 处）（治理项 R2）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| System 九值枚举 | src/Core/Gameplay/GAS/GasDiagnosticEvents.cs:5-16 |
| Metric 20 命名值与缺号 | src/Core/Gameplay/GAS/GasDiagnosticEvents.cs:18-40 |
| 事件结构与缓冲（默认 32、满抛） | src/Core/Gameplay/GAS/GasDiagnosticEvents.cs:42-105 |
| 报告系统（发布方） | src/Core/Gameplay/GAS/Systems/GasBudgetReportSystem.cs:32-67 |
| 缓冲创建与服务注册 | src/Core/Engine/GameEngine.cs:700、1465、1856 |
| 错误码字面量散布（R2 证据） | 全 src 扫描：`GAS.*.ERR.*`；典型 src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs |

**相关文档**：[rt-03 PRD](../prd/rt-03-diagnostics.md) · [rt-02 reference](rt-02-budgets.md)
