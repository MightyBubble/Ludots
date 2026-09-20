# gr-08 runtime spec · 挂接点总表

> 引擎实现任务书。第一性需求见 [gr-08 PRD](../prd/gr-08-mount-points.md)；现状见 [reference](../reference/gr-08-mount-points.md)。

## 1. 概述

八主挂点与五次要挂点的 kind 合同、消费入口、终检语义。

## 2. 设计

- 八主挂点保持各自 kind 合同：效果相位按相位分家（OnPropose→Validation、其余→Effect）；监听叠加纯度闸；派生 Derived；前置/订单 Validation；打分 Score；BT 叶 Script 可挂起；HFSM Script 禁挂起。
- 五次要挂点保持：关卡脚本（Script、步数预算 64、禁挂起）、进度校验 Validation、表现规则 Validation+Score、瞄准预览 Query、查询物化 Query。
- 挂接终检统一走 RequireKind 语义：图未注册、kind 不符、空程序三类拒绝。

## 3. 精确语义与不变量

- 每个挂点的 kind 合同封闭，运行期不再二次判定；全部挂接在装载完成后才可触发。

## 4. 迁移与治理

现状即基线；治理项 G1（kind 不符文案中英混杂）见 todo/graph.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-08 PRD](../prd/gr-08-mount-points.md) · [reference](../reference/gr-08-mount-points.md) · [gr-09 spec](gr-09-outputs.md)
