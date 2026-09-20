# attr-05 reference · 属性绑定与 Sink

> 现状参考。第一性需求见 [attr-05 PRD](../prd/attr-05-bindings.md)；配置说明见 [attr-05 配置说明](../config/attr-05-bindings.md)。

## 1. 现状快照

- 绑定表现状 16 条：`Bind.Physics.ForceInput2D.X/Y` 两条 + `Bind.CameraBehavior.*` 14 条（通道 0-14 中 14 个），全部 Override/scale 1.0/ResetToZeroPerLogicFrame。
- 内置 sink 三个：ForceInput2DSink 与 CameraBehaviorInputSink（GasAttributeSinks.RegisterBuiltins）+ GraphEdgeCostOverlaySink（sink 名 Graph.EdgeCostOverlay），注册后 Freeze。Graph.EdgeCostOverlay 现状零内容绑定。
- loader 七字段全 RequireExplicit：id/attribute/sink/channel（0..255 再交 sink 复核）/mode（仅 Add、Override）/scale（有限数）/resetPolicy（仅 None、ResetToZeroPerLogicFrame）。合并后按 Id Ordinal 排序遍历，按 (SinkId,Order) 折叠成 AttributeBindingGroup 落盘。
- ForceInput2DSink：查 AttributeBuffer+ForceInput2D 实体，通道仅 0/1；Apply 先按 resetPolicy 清零 Fix64 分量再逐绑定写（Override 替换/Add 累加），float→Fix64 边界转换；脉冲策略把源属性 SetCurrent(0)。reset 判定只看条目自身。
- CameraBehaviorInputSink：要求场上恰好一个 AttributeBuffer+CameraBehaviorInputTarget 实体（数量≠1 抛，该实体由引擎创建）；状态每帧无条件全清+Revision++；通道 0-14（MoveX/MoveY/PointerX/PointerY/PointerDeltaX/Y/LookX/LookY/Zoom/RotateHold/RotateLeft/RotateRight/GrabDragHold/FollowHold/PointerActive）；bool 阈值 |v|>0.0001 且 Add=OR；脉冲同样回写属性 0。
- 驱动：AttributeBindingSystem（AttributeCalculation，聚合之后）逐组 Apply。反向（输入→属性）另一套：InputActionAttributeBindingSystem（InputCollection）+ Input/action_attribute_bindings.json。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 绑定表现状与目录登记 | assets/GAS/attribute_bindings.json:1-155；assets/config_catalog.json:131-134 |
| loader 字段合同 | src/Core/Gameplay/GAS/Bindings/AttributeBindingConfig.cs:5-14 |
| RequireExplicit 与排序折叠 | src/Core/Gameplay/GAS/Bindings/AttributeBindingLoader.cs:30-114,116-229 |
| 内置 sink 注册与冻结 | src/Core/Gameplay/GAS/Bindings/GasAttributeSinks.cs:11-24；src/Core/Engine/GameEngine.cs:1306-1311 |
| 第三 sink（导航图） | src/Core/Navigation/GraphSemantics/GAS/GraphAttributeSinks.cs:8-11 |
| 力输入 sink | src/Core/Gameplay/GAS/Bindings/ForceInput2DSink.cs:17-95 |
| 相机 sink 与通道表 | src/Core/Gameplay/GAS/Bindings/CameraBehaviorInputSink.cs:26-58,94-155 |
| 相机状态每帧清零 | src/Core/Gameplay/Camera/CameraBehaviorInputState.cs:71-85 |
| 相机目标实体创建 | src/Core/Engine/GameEngine.cs:1305 |
| 绑定系统驱动 | src/Core/Gameplay/GAS/Systems/AttributeBindingSystem.cs:18-28；GameEngine.cs:1841 |
| 反向体系 | src/Core/Input/Systems/InputActionAttributeBindingSystem.cs；GameEngine.cs:1694 |

**相关文档**：[attr-05 PRD](../prd/attr-05-bindings.md) · [attr-03 reference](attr-03-aggregation.md)
