# input-05 reference · 过滤与输入方案

> 现状参考。第一性需求见 [input-05 PRD](../prd/input-05-filters-and-schemes.md)；配置说明见 [input-05 配置说明](../config/input-05-filters-and-schemes.md)。

## 1. 现状快照

- `default_input.json`：`InputActionDef{id,name,type Button|Axis1D|Axis2D|Axis3D}` + `contexts{id,name,priority,bindings{actionId,path,compositeType,compositeParts,processors}}`。根资产 22 动作 + 2 上下文（Default_Gameplay / Physics2D_Playground）；Hotkey1-9、PrimaryClick 等关键动作只绑在 Physics2D_Playground（Default_Gameplay 未绑）。
- `filter_profiles.json`：`profiles[].id` / `associationQuery{anchor:"localPlayerRep", expand:"controls"|"none"}` / `exclude.anyTags` / `include.anyTags`；消费方为 ContextBoundCollectionWriter 与交互上下文 filterProfileId。根资产一档案（filter.controllable.default）。
- `control_schemes.json`：`schemes[]{id, inputContexts[], 可选 axisMove{actionId, orderTypeKey, throttleTicks, stepDistanceCm}}` + 根级 `allowedSchemes`（空=全允许）；axisMove 由 AxisMoveOrderSystem 节流提交。根资产 scheme.default。方案不携带下单偏好：`interaction_prefs.json` 的 `defaults{commandIntentId, castDispatchProfileId}`（根资产指向 intent.command.default + dispatch.all_together）在进图绑定期种到 representative 的 InteractionPref 组件。
- `action_attribute_bindings.json`：全字段显式必填（id/action/attribute/valueKind/sourceChannel/target/scale/zeroWhenUiCaptured/suppressOnUiWheelCaptured/preserveValueUntilSnapshot）；InputActionAttributeBindingSystem 把动作值写入 AttributeBuffer。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 动作/上下文模型 | src/Core/Input/Config/InputConfigModels.cs |
| 关键动作缺口（O9） | assets/Input/default_input.json（两上下文绑定对照） |
| 过滤档案形状 | src/Core/Input/Interaction/FilterProfile.cs:33-58 |
| 过滤消费 | src/Core/Input/Interaction/ContextBoundCollectionWriter.cs:68 |
| 方案形状与运行时 | src/Core/Input/Interaction/ControlScheme.cs:5-67,111,157 |
| 轴移动系统 | src/Core/Input/Systems/AxisMoveOrderSystem.cs |
| 属性绑定加载（全显式） | src/Core/Input/Attributes/InputActionAttributeBindingLoader.cs:25-94 |
| 属性绑定系统 | src/Core/Input/Systems/InputActionAttributeBindingSystem.cs |
| 根资产 | assets/Input/ 下四文件 |

**相关文档**：[input-05 PRD](../prd/input-05-filters-and-schemes.md) · [ord-06 reference](ord-06-input-mappings.md)
