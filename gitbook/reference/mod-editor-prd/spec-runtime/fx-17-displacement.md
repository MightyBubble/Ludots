# fx-17 runtime spec · 位移

> 引擎实现任务书。第一性需求见 [fx-17 PRD](../prd/fx-17-displacement.md)；现状见 [reference](../reference/fx-17-displacement.md)。

## 1. 概述
位移段合同：方向解析、状态组装、同目标替换语义、独占计划。

## 2. 设计
- HandleApplyDisplacement：方向目标与目标点按 上下文目标实体 → 保留参数 → 施法实例 TargetPos 优先级解析，组装 DisplacementState（距离/时长/剩余量/模式/导航压制）。
- 替换合同：TryReplaceActiveDisplacement 就地覆写同目标的活跃位移段（保留写权窗口请求位、按需撤销旧段的移动压制），不新建第二状态。
- 注册为 External(Displacement)：效果计划独占；上限约束单段位移（时长/距离），不约束历史累计。

## 3. 精确语义与不变量
- 一个目标至多一条活跃 DisplacementState；新位移必替换或新建，二者必居其一。
- 两个驱动者不得同时写同一实体位姿——替换合同是该禁令的执行机制。
- 时钟刷新由位移系统在写权确认后执行，处理器不直接改位姿。

## 4. 迁移与治理
现状即基线。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-17 PRD](../prd/fx-17-displacement.md) · [reference](../reference/fx-17-displacement.md)
