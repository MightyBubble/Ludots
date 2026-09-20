# gr-07 reference · 动作库 ActionLib

> 现状参考。第一性需求见 [gr-07 PRD](../prd/gr-07-actionlib.md)；配置说明见 [gr-07 配置说明](../config/gr-07-actionlib.md)。

## 1. 现状快照

- JSON 字段现状：name / graph / kind（必须 Script）。host 字段已删除（#1542 资产中性），条目不声明消费方。
- 撞名现状：动作名不得与 FuncLib 重复，双向检查。
- 政策现状：装载期不做 Yield 校验；消费方（BT 叶 / HFSM 生命周期 / 脚本挂接点）在绑定时校验自身挂起约束。
- 装载位置现状：FuncLib 装载与调用终检之后（GameEngine 装载链）。
- 资产现状：action_lib 7 条——3 BehaviorTree 叶 / 3 HFSM 生命周期 / 1 Script。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 字段与撞名门 | src/Core/NodeLibraries/GASGraph/Host/GraphActionCatalogLoader.cs |
| 资产中性（消费侧验证） | GraphActionCatalogLoader.cs（Register 调用处的注释与合同） |
| 消费方绑定校验 | GraphBehaviorDefinitionLoader / 各 BrainHostSystem 绑定路径 |
| 装载链位置 | src/Core/Engine/GameEngine.cs |
| 资产 | assets/GAS/action_lib.json |

**相关文档**：[gr-07 PRD](../prd/gr-07-actionlib.md) · [gr-05 reference](gr-05-execution.md) · [gr-08 reference](gr-08-mount-points.md)
