# GAS Composition Gate — Graph Codegen 运行时装载

## 任务摘要

在 #1274 合入后，把 Codegen 接到引擎装图与执行入口：`graphExecutionBackend`、生成入口挂登记表、`GraphExecutor` 优先生成码、Live Debug / AgentBridge 报后端、夜袭旗舰强制 codegen。

## 判断标准结论

**通过。** 不新增 opcode / 作者格式；只换同一 IR 的执行后端并接线装载模式。

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | GraphProgramRegistry、GraphExecutor、GameConfig、AgentBridge graph.debug、Ludots.Graph.Codegen |
| 新增 | IGraphCodegenRuntimeBinder、Registration.Generated*、ExecuteSlice 生成入口、game.json graphExecutionBackend |
| 边界 | Core 不引用 Roslyn；通过程序集解析加载 Graph.Codegen |
