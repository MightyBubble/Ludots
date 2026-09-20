# GAS Composition Gate — Graph Codegen 实现（CG-0…CG-6）

## 任务摘要

把图 Codegen 从 GasTests 尖峰升格为产品后端：独立 `Ludots.Graph.Codegen` 程序集、全量 op 发射策略（F0–F3 特化 + 其余 HandlerForward）、coverage `codegenStatus`、Bridge 预览/对拍/覆盖 API、编辑器 Codegen 面板、对拍与策略门禁。

## 判断标准结论

**通过。** 不新增 profile enum / preset 开关；不新增平行 opcode；只对同一份 `GraphInstruction[]` 增加可替换执行后端与可视化合同落地。

## 自审清单

| 项 | 结论 |
|----|------|
| 新变体是 op 组合还是 enum/开关？ | 都不是：发射策略表 + HandlerForward 复用既有 handler |
| 是否平行加载器/第二作者格式？ | 否；FrontDoor → 同一 IR |
| 失败关闭？ | 是；未知 op / 编译失败不静默回落解释器 |
| Roslyn 边界？ | 独立 `Ludots.Graph.Codegen`，不塞进 Core 热路径 |

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | `GasGraphOpHandlerTable`、`GraphExecutionState`、`GraphInstruction`、coverage registry、Bridge validate 编译前门、编辑器 `/gas-graphs` |
| 新增 | `Ludots.Graph.Codegen`（Emitter/Host/Strategy/Parity/Coverage）、`RunToHalt`/`RunSlice` 公开入口、Bridge codegen API、`GraphCodegenPanel` |
| 禁止未做 | 平行 opcode、静默 interpret fallback、在生成码里直写 World |

## 切片落地

- CG-0：程序集 + coverage 字段 + 面板壳
- CG-1…CG-3：F0–F3 特化（含回边）
- CG-4…CG-6：其余家族 HandlerForward + 覆盖门禁全绿 + 尖峰薄委托
