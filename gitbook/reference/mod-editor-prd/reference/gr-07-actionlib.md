# gr-07 reference · 动作库 ActionLib

> 现状参考。第一性需求见 [gr-07 PRD](../prd/gr-07-actionlib.md)；配置说明见 [gr-07 配置说明](../config/gr-07-actionlib.md)。

## 1. 现状快照

- JSON 字段现状：name / graph / kind（必须 Script）/ host（必填，四值 BehaviorTree、Hfsm、Script、MapTrigger）。
- 撞名现状：动作名不得与 FuncLib 重复，双向检查。
- 政策现状：仅 BehaviorTree 与 Script 允许挂起；Hfsm/MapTrigger 动作装载时经 GraphYieldPurityValidator 做可达 Yield 校验，违规报路径。
- 装载位置现状：FuncLib 装载与调用终检之后（GameEngine 装载链）。
- 资产现状：action_lib 10 条——5 BehaviorTree / 4 Hfsm / 1 Script。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 字段与 host 门 | src/Core/NodeLibraries/GASGraph/Host/GraphActionCatalogLoader.cs:65-116 |
| 撞名检查 | GraphActionCatalogLoader.cs:65-68 |
| 挂起可达校验（复用纯度器） | GraphActionCatalogLoader.cs:104-114 |
| 宿主枚举与政策 | src/Core/GraphRuntime/GraphActionHost.cs:5-17 |
| 装载链位置 | src/Core/Engine/GameEngine.cs:897-908 |
| 资产 | assets/GAS/action_lib.json |

**相关文档**：[gr-07 PRD](../prd/gr-07-actionlib.md) · [gr-05 reference](gr-05-execution.md) · [gr-08 reference](gr-08-mount-points.md)
