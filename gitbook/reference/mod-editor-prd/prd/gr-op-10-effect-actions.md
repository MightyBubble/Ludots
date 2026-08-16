# gr-op-10 · 节点：效果与事件动作

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-10-effect-actions.md)；编辑器需求见 [UXD](../uxd/gr-op-10-effect-actions.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-10-effect-actions.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-10-effect-actions.md)；现状见 [reference](../reference/gr-op-10-effect-actions.md)。

## 1. 定位

Effect 图的动作面九件：上效果（模板/动态、单体/扇出）、按派发预设扇出、撤效果、直改属性、发事件。图里一切"做事"的出口。

## 2. 产品承诺

- **模板与动态两路**：ApplyEffectTemplate 按符号模板上效果，a/b 引脚直通模板的力场参数；ApplyEffectDynamic 按 value 里的模板号上效果——配置出号，图只管发。
- **扇出四件**：FanOut 系把动作派给查询链选出的整列表；Dispatch 两件按派发预设（contextMapping/payloadEffect）分派。
- **直改属性**：ModifyAttributeAdd 对目标属性加值——走效果提案，与 WriteSelfAttribute 的直写口（gr-op-04）分工。
- **发事件**：SendEvent 对目标发带值的事件，消费方是 Reaction/EventGate。
- **Effect 图专属且事务型**：九件只在 Effect 图；全部在效果事务内提案，失败随事务回滚。

## 3. 运行行为

模板应用继承调用方根 id（RootId），力参数走 CallerParams 的 ForceX/Y 通道；扇出受单根 fan-out 预算约束（见事实页）；ModifyAttributeAdd 进入效果修改器管线聚合。

## 4. 异常承诺

模板/派发预设/事件 tag/属性符号未注册——编译失败并指明节点与符号。扇出超预算——运行失败并报预算计数。非 Effect 图使用——编译拒绝。

**相关文档**：[配置说明](../config/gr-op-10-effect-actions.md) · [fx-11](fx-11-target-dispatch.md) · [gr-op-04](gr-op-04-attributes.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
