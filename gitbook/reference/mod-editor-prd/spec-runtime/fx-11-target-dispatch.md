# fx-10 runtime spec · 目标派发

> 引擎实现任务书。第一性需求见 [fx-10 PRD](../prd/fx-11-target-dispatch.md)；现状见 [reference](../reference/fx-11-target-dispatch.md)。

## 1. 概述

派发映射、预设表与 FanOut 命令链合同。

## 2. 设计

- 映射合同保持：preset 与 contextMapping 互斥；双缺省走默认映射（Source=原施法者、Target=解析实体、TargetContext=原目标）；槽值域四值；payloadEffect 引用注册表。
- 内建链保持：纯查询处理器只写候选数；派发处理器做过滤+根预算+命令缓冲；二合一处理器合并两步。
- 图路径保持：扇出图 op 走运行时 API——事务内暂存随提交发布、无事务直发；命令落地按三槽重映射发布；预设表全字段必填、加载序先于效果表。

## 3. 精确语义与不变量

- 事务失败时该事务内的扇出命令全部不发布；载荷请求携带重映射后的三槽，不再携带原查询上下文。

## 4. 迁移与治理

现状即基线，无迁移项。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-10 PRD](../prd/fx-11-target-dispatch.md) · [reference](../reference/fx-11-target-dispatch.md)
