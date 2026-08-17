# ai-08 editor spec · 战斗姿态与执行器门

> 编辑器实现任务书。编辑器需求见 [ai-08 UXD](../uxd/ai-08-stances-actuators.md)；引擎侧见 [runtime spec](../spec-runtime/ai-08-stances-actuators.md)。

## 1. 概述

许可层面板实现：姿态表单 + 半成品警示、执行器表单 + 门控试验、组件注入联动。

## 2. 设计

- **半成品警示**：姿态区常驻条（文案与 runtime spec 同源：编译保留、无系统消费）；I6 接线落地后此条移除。
- **门控试验**：本地重放门控判定（同源函数），支持注入样例组件值。
- **注入联动**：ActuatorReadiness/AimGate 组件编辑直通实体模板面板。
- **引用校验**：过滤器/输入引用下拉化并预检。

## 3. 精确语义与不变量

- 门控试验结果与 PassesActuatorGates 原因码一致。
- 落盘字段与 CompileStances/CompileActuators 解析名一致。

## 4. 依赖接口与验收

- 消费：stances/actuators 合并视图、inputs/target_filters/abilities 三注册视图、实体模板组件 schema。
- 验收：半成品警示在空表与非空表均呈现；门控试验可复现 AimGateNotReady；组件注入保存后实体查询可见。

**相关文档**：[ai-08 UXD](../uxd/ai-08-stances-actuators.md) · [ai-08 runtime spec](../spec-runtime/ai-08-stances-actuators.md)
