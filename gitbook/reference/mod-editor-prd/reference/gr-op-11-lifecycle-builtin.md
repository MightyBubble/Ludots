# gr-op-11 reference · 节点：生命周期与内建

> 现状参考。第一性需求见 [gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md)；配置说明见 [gr-op-11 配置说明](../config/gr-op-11-lifecycle-builtin.md)。

## 1. 现状快照

- BeginLifecycleTransaction（:177，Effect）：效果组合元数据按 Lifecycle 域 Unsupported（fail-closed）。
- InvokeBuiltin（:178，Effect，imm=handler 符号，DelegatedBuiltin）。
- 内建 20 个，编号分段：ApplyModifiers=1；SpatialQuery/DispatchPayload/ReResolveAndDispatch=10-12；ApplyForce=20；CreateProjectile/CreateUnit=30-31；ApplyDisplacement=40；ApplyRelation/RevealArea/DecayRevealArea=50-52；ExecuteExchange/CompleteProgression/SubmitOrderFromBlackboard=60-62；MaterializeTemplate/CopyIdentityComponents/CopyAttributeSlice/ClearActiveEffects/TransferStableId=63-67、ConsumeEntity=69。
- 引擎默认 `Graph.Lifecycle.DeployConsumeSource`（assets/GAS/graphs.json）为七节点事务链。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 两 op 描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:177-178 |
| Lifecycle 域 fail-closed | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs:183-184 |
| 内建 id 全表 | src/Core/Gameplay/GAS/BuiltinHandlerId.cs:8-63 |
| 内建注册表 | src/Core/Gameplay/GAS/BuiltinHandlerRegistry.cs |
| 生命周期处理器 | src/Core/Gameplay/Lifecycle/EntityLifecycleBuiltinHandlers.cs |
| 默认部署链 | assets/GAS/graphs.json（Graph.Lifecycle.DeployConsumeSource） |

**相关文档**：[gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md) · [gr-op-12 reference](gr-op-12-placement.md)
