# fx-23 runtime spec · 出生下单

> 引擎实现任务书。第一性需求见 [fx-23 PRD](../prd/fx-22-submit-order.md)；现状见 [reference](../reference/fx-22-submit-order.md)。

## 1. 概述
黑板读出与订单提交合同：五键快照、按目标种类组单、正式入口准入。

## 2. 设计
- HandleSubmitOrderFromBlackboard：解析 source（黑板宿主）与 target（执行者），BlackboardStoredTargetOps.TryRead 五键快照；无目标静默返回。
- 组单规则：Point/HexCell → 点移动单（WorldCm 空间参数，Single 收集模式）；Entity → 实体单（Target + Args.I0）；玩家 id 取自执行者。
- 提交走正式 OrderQueue 入口 SubmitAssigned，非接受结果即抛错（带订单 id 与结果枚举）；执行者必须已有 OrderBuffer；注册为 External(Order) 独占。

## 3. 精确语义与不变量
- 一次效果执行至多一条订单；与玩家命令走同一准入与终态链路。
- 黑板无目标不是错误：跳过且不产生副作用。
- 提交被拒是错误：抛错中断效果链（与 fx-23 兑换的"失败不抛"对照，两域语义各自决定）。

## 4. 迁移与治理
现状即基线。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-23 PRD](../prd/fx-22-submit-order.md) · [reference](../reference/fx-22-submit-order.md)
