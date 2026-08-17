# fx-09 runtime spec · 提案窗口与 Instant 内联

> 引擎实现任务书。第一性需求见 [fx-08 PRD](../prd/fx-06-proposal-window.md)；现状见 [reference](../reference/fx-06-proposal-window.md)。

## 1. 概述

纯相位 fail-closed 编译、验证语义与外部原子独占律的合同。

## 2. 设计

- 编译期检查保持：纯相位（提案/计算）非纯操作计数>0 即抛；监听图禁内建调用与配置读；纯相位监听图只许纯图、非纯相位只许纯/事务图。
- 验证语义保持：结果寄存器执行前播种"否"，图内须显式写"是"；拒绝粘滞于窗口；空相位直接通过；四窗口（激活=裁决+命中+应用 / 周期 / 过期 / 移除）全 finalized 才可用。
- 独占律保持：仅激活窗口允许恰一个外部原子——须 Instant、零事务图、最后一步、不与 modifiers/grantedTags/监听器设置组合；违反抛组合违例，运行期监听器预检冲突照抛。

## 3. 精确语义与不变量

- 外部原子域是闭集：位移、进度、订单三类之外一律 fail-closed（含图侧生命周期事务与关系修改 op）；独占律成立的窗口失败时回滚边界唯一。

## 4. 迁移与治理

现状即基线，无迁移项。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-08 PRD](../prd/fx-06-proposal-window.md) · [reference](../reference/fx-06-proposal-window.md)
