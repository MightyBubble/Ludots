# gr-op-10 reference · 节点：效果与事件动作

> 现状参考。第一性需求见 [gr-op-10 PRD](../prd/gr-op-10-effect-actions.md)；配置说明见 [gr-op-10 配置说明](../config/gr-op-10-effect-actions.md)。

## 1. 现状快照

- 九件全 Effect 掩码：ApplyEffectTemplate（:119，target+a+b+模板符号；a/b→CallerParams ForceX/Y，RootId 继承）；FanOutApplyEffect（:120，imm）；ApplyEffectDynamic（:121，target+value）；FanOutApplyEffectDynamic（:122，value）；RemoveEffectTemplate（:123，target+imm）；FanOutDispatchEffect（:124，dst=派发预设+imm）；FanOutDispatchEffectDynamic（:125，value+dst）；ModifyAttributeAdd（:126，target+value+属性符号）；SendEvent（:127，target+value+事件 tag 符号）。
- 扇出受单根 fan-out 上限（事实页 rt-02 域）；SendEvent 走帧延迟事件总线。
- 符号四表：效果注册表、派发预设、事件 tag、属性注册表。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Apply 模板/动态与扇出 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:119-122 |
| Remove 与 Dispatch 两件 | GraphOpDescriptorTable.Data.cs:123-125 |
| ModifyAttributeAdd 与 SendEvent | GraphOpDescriptorTable.Data.cs:126-127 |

**相关文档**：[gr-op-10 PRD](../prd/gr-op-10-effect-actions.md) · [gr-op-11 reference](gr-op-11-lifecycle-builtin.md)
