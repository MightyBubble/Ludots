# gr-02 reference · 图文档格式

> 现状参考。第一性需求见 [gr-02 PRD](../prd/gr-02-document.md)；配置说明见 [gr-02 配置说明](../config/gr-02-document.md)。

## 1. 现状快照

- 顶层七字段：Id/Kind/Entry/Nodes/ControlEdges/ValueEdges/Outputs。
- 节点字段现状全表（camelCase 落盘）：id、op、intValue、floatValue、boolValue、graphId（与 functionName 互斥）、attribute、tag、template、collectionKey、effectTemplate、payloadPreset、builtinHandler、blackboardKey、configKey、relationshipType、relationshipMode、metric、flag、slot、queryCapacityPolicy、droppedOutput、validOutput、形状族（radiusCm…hexRadius）、layerMask、teamId、descending、pinRegister（缺省 -1，仅 int 类）。
- 端口常量集：enter/next/true/false/body/call/target/value/list/teamId/source/min/max/a/b/condition/selector/default + `case:` 前缀动态端口。
- 创作门现状：kind 必填且 ControlFlow 可创作才收；节点 next 硬拒；两键边表必须存在；id 缺省补、引用大小写不敏感。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 顶层七字段 | src/Core/GraphRuntime/GraphControlFlowDocument.cs:12-21 |
| 节点字段全表 | GraphControlFlowDocument.cs:23-69 |
| 端口常量集 | GraphControlFlowDocument.cs:109-153 |
| kind 必填与创作门槛 | src/Core/NodeLibraries/GASGraph/Host/GraphProgramAuthoringFrontDoor.cs:69-90 |
| next 硬拒 | GraphProgramAuthoringFrontDoor.cs:94-125 |
| 双边键强制 | GraphProgramAuthoringFrontDoor.cs:101-106 |
| id 补全与大小写 | GraphProgramAuthoringFrontDoor.cs:51-59 |
| 真实文档实例 | assets/GAS/graphs.json |

**相关文档**：[gr-02 PRD](../prd/gr-02-document.md) · [gr-01 reference](gr-01-model.md) · [gr-04 reference](gr-04-compilation.md)
