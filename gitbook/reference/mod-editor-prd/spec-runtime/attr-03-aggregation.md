# attr-03 runtime spec · 聚合管线

> 引擎实现任务书。第一性需求见 [attr-03 PRD](../prd/attr-03-aggregation.md)；现状见 [reference](../reference/attr-03-aggregation.md)。

## 1. 概述

重算管线三步序（复位→叠加→派生）与 Cap/Current 双轨的语义合同。

## 2. 设计

- 系统位置保持：AttributeCalculation 组内先于属性绑定与相机，双查询对无脏组件实体直接抛错；重算步序保持复位→叠加→派生。
- 双轨保持：重算后对定义位每属性令 Cap=重算 Current；被派生写过的位不恢复持久 Current，其余恢复旧持久值。
- 打脏点集合保持：效果应用/入栈/移除/图内取消/事务取消/装备授予六类。
- **治理项 A4**：聚合资格由 Buff 预设隐式推导——preset 面显式化，新预设强制显式声明。
- **治理项 A5/A6**：无 snapshot 实体首帧 OldValue=0 伪基线（补建对齐实体创建路径）；聚合器构造允许图程序表为 null（构造期校验，缺失即启动失败）。

## 3. 精确语义与不变量

- 聚合脏标记一次性：值有变化才打属性脏与表现位，消费即移除；叠加资格在重算时逐条复核（存活/提交/未取消/聚合标志），效果容器里的陈旧条目不参与。

## 4. 迁移与治理

现状即基线；A4-A6 见 todo/attribute.md。A4 属配置面扩展，随 preset 声明面治理批次；A5/A6 为健壮性补丁可独立落地。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[attr-03 PRD](../prd/attr-03-aggregation.md) · [attr-04 runtime spec](attr-04-derived.md)
