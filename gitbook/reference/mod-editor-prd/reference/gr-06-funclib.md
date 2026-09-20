# gr-06 reference · 函数库 FuncLib

> 现状参考。第一性需求见 [gr-06 PRD](../prd/gr-06-funclib.md)；配置说明见 [gr-06 配置说明](../config/gr-06-funclib.md)。

## 1. 现状快照

- JSON 字段现状：name（合并键）、graph（必填）、kind（必填且仅 Script，报错文案注明 Score/Validation 延后）、purity（可选，仅 "pure"）。
- 图引用现状：graph 须先注册且 kind 与条目一致，否则拒。
- 纯度闭包现状：TryValidateNoReachableYield 图可达性遍历（Jump/JumpIfFalse/Call/InvokeScript），三类拒绝——可达挂起、跨图调用环（InvokeCycle）、非法闭包；挂起报错指引 ActionLib。
- 装载顺序现状：graphs → PatchAndRegister → FuncLib Load → ResolveFuncLibInvokes + 终检 → ActionLib。
- 资产现状：func_lib 3 条（demo.const.seven / ability.slash / ability.bash）；GraphFunctionCatalog.Register 接受 Script/Validation/Score，loader 只喂 Script。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 字段与 kind/purity 门 | src/Core/NodeLibraries/GASGraph/Host/GraphFunctionCatalogLoader.cs:84-112 |
| 纯度校验触发 | GraphFunctionCatalogLoader.cs:135-144 |
| 闭包遍历算法 | src/Core/NodeLibraries/GASGraph/Host/GraphYieldPurityValidator.cs:118-328 |
| Register 三 kind 死路径 | src/Core/GraphRuntime/GraphFunctionCatalog.cs:22 |
| 装载链位置 | src/Core/Engine/GameEngine.cs:834,897-908 |
| 资产 | assets/GAS/func_lib.json |

**相关文档**：[gr-06 PRD](../prd/gr-06-funclib.md) · [gr-04 reference](gr-04-compilation.md) · [gr-07 reference](gr-07-actionlib.md)
