# gr-op-12 runtime spec · 节点：放置校验

> 引擎实现任务书。第一性需求见 [gr-op-12 PRD](../prd/gr-op-12-placement.md)；现状见 [reference](../reference/gr-op-12-placement.md)。

## 1. 概述

落点校正合同：原地改 TargetPos 的三件、纯谓词一件、图边投影查询。

## 2. 设计

- Clamp/吸附对 TargetPos 的修改保持"原地一次写"：同一图内多次校正按控制流顺序最后写入生效。
- SnapToNearestGraphEdge 的投影查询走 GraphEdgeProjectionQuery 专用通道，不复用空间检索。
- 集合吸附的 `validOutput` 是命名有效口：布尔结果落指定暂存，实体落目的寄存器。
- **治理项**：Script 图落点校验空档——四件掩码不含 Script，AI 行为树叶子需要校验落点时无件可用；有实场景再扩掩码。

## 3. 精确语义与不变量

- IsPointInCircle 永不改状态；Clamp/吸附只在求值时改一次 TargetPos。
- 吸附失败不报错：集合吸附出无效句柄，边吸附返回假。
- 集合键解析失败即整图失败。

## 4. 迁移与治理

现状即基线；Script 掩码观察项入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-12 PRD](../prd/gr-op-12-placement.md) · [reference](../reference/gr-op-12-placement.md)
