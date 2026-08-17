# ai-10 reference · 层次状态机

> 现状参考。第一性需求见 [ai-10 PRD](../prd/ai-10-hfsm.md)；配置说明见 [ai-10 配置说明](../config/ai-10-hfsm.md)。

## 1. 现状快照

- 配置：主仓 `assets/AI/hfsm.json` 两台——hfsm.sentry（纯谓词版）与 hfsm.sentry.scripted（combat 态挂 onEnter/onTick/onExit + condition 图）；各 6 状态 4 转移。schema 存在（state kind Leaf/Compound、predicate Never/Always/StimulusLatched、transitions from/to/predicate 必填 + priority/condition 可选），不参与流水线校验（I10）。
- 加载：Compound 须 defaultChild、禁多父禁不可达、Leaf 不得有 children；枚举 ignoreCase:false（I2）；图名 Require(host=Hfsm)。
- 执行：叶子态优先再上爬；同 from 按 priority 降序、平级**后定义者胜**（I8）；StimulusLatched 触发后清零；条件图 ReturnInt≠0；生命周期图 64 步预算禁 Yield（未 halt 报错，两指令+halt 有快路径，程序缓存 8）；onEnter/onExit 随 LCA、onTick 每波。
- 上限：MaxStates=64、MaxTransitions=128、MaxStackDepth=8。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 上限常量 | src/Core/Gameplay/AI/Fsm/HfsmOps.cs:81-83 |
| 结构校验 | src/Core/Gameplay/AI/Fsm/HfsmDefinition.cs:15-76 |
| HFSM 解析（含 schema 对应字段） | src/Core/Gameplay/AI/Config/GraphBehaviorDefinitionLoader.cs:194-443 |
| 转移择优与平局 | src/Core/Gameplay/AI/Fsm/HfsmWorld.cs:125-179 |
| LCA 生命周期 | HfsmWorld.cs（ApplyTransition/ExitUpTo/EnterDownFrom） |
| Stimulus 置位/清零 | HfsmWorld.cs:40-43,175-178 |
| 生命周期图预算与缓存 | src/Core/Gameplay/AI/Fsm/GraphProgramHfsmHost.cs:10-72 |
| 资产与 schema | assets/AI/hfsm.json、assets/AI/hfsm.schema.json |

**相关文档**：[ai-10 PRD](../prd/ai-10-hfsm.md) · [ai-11 reference](ai-11-goap-htn.md)
