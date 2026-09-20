# ai-08 runtime spec · 战斗姿态与执行器门

> 引擎实现任务书。第一性需求见 [ai-08 PRD](../prd/ai-08-stances-actuators.md)；现状见 [reference](../reference/ai-08-stances-actuators.md)。

## 1. 概述

许可层合同：执行器门控消费路径完整；姿态编译保留、消费缺失。

## 2. 设计

- PassesActuatorGates 保持：读实体 AimGate/ActuatorReadiness 组件 + 执行器定义，未就绪带 UtilityAiReadinessBlockReason。
- 组件注入路径保持（实体配置可写 ActuatorReadiness/AimGate）。
- **治理项（引 todo/ai.md）**：I6——stance 半成品：Stances 编译、DefaultStance 解析、UtilityAiStanceState 组件存在，但无系统读写（仅 AIInspector 打印长度）；立项消费系统（索敌/反击/追击许可并入过滤器或决策就绪）或冻结声明；I7——两个 showcase 的 stances/actuators 为空 [] 占位文件，随 I6 一并处置。

## 3. 精确语义与不变量

- 门控失败 ⇒ 决策不计入就绪（readinessBlockReason 可 trace），不提交任务。
- stance 现状不影响任何运行行为——这是文档合同而非缺陷遮掩。

## 4. 迁移与治理

现状即基线；I6/I7 处置入 todo/ai.md。姿态消费系统落地时须同步 ai-05 DefaultStance 语义与 ai-06 过滤器联动。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-08 PRD](../prd/ai-08-stances-actuators.md) · [reference](../reference/ai-08-stances-actuators.md)
