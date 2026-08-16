# gr-05 runtime spec · 函数库 FuncLib

> 引擎实现任务书。第一性需求见 [gr-05 PRD](../prd/gr-06-funclib.md)；现状见 [reference](../reference/gr-06-funclib.md)。

## 1. 概述

函数目录合同：字段封闭、纯度闭包校验、装载链位置。

## 2. 设计

- 字段四件封闭（name/graph/kind/purity）；name 为合并键；kind 门保持仅 Script；purity 仅 pure。
- 纯度闭包校验保持图可达性遍历（跳转/条件跳/调用/跨图调用），三类拒绝：可达挂起、跨图调用环（InvokeCycle）、非法闭包；错误信息保持"挂起属于 ActionLib"指引。
- 装载链位置固定：graphs 之后、ActionLib 之前；装载后统一回写调用点并终检（gr-03 patch）。

## 3. 精确语义与不变量

- 入库函数从入口不可达挂起（含经被调子图）；函数名与动作名两个命名空间不重叠；装载完成后程序内无未解析函数名。

## 4. 迁移与治理

现状即基线；治理项 G5（Register 层接受三 kind 而 loader 只喂 Script 的死路径）见 todo/graph.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-05 PRD](../prd/gr-06-funclib.md) · [reference](../reference/gr-06-funclib.md) · [gr-06 spec](gr-07-actionlib.md)
