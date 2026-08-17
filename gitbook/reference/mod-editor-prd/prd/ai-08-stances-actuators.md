# ai-10 · 战斗姿态与执行器门

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-08-stances-actuators.md)；编辑器需求见 [UXD](../uxd/ai-08-stances-actuators.md)；引擎实现见 [runtime spec](../spec-runtime/ai-08-stances-actuators.md)；editor spec 见 [editor spec](../spec-editor/ai-08-stances-actuators.md)；现状见 [reference](../reference/ai-08-stances-actuators.md)。

## 1. 定位

stance 描述单位的交战姿态（自动索敌、反击、追击许可 + 专属目标过滤器），actuator 描述技能出手前的就绪门槛（就绪输入 + 瞄准门输入）。两者是效用决策的"许可层"：姿态管打谁，执行器管能不能打。

## 2. 产品承诺

- **姿态可声明**：TargetFilter + 三个布尔许可；profile 的 DefaultStance 语义键绑定（数字 id 显式拒绝）。
- **执行器门可配**：ReadinessInput/AimGateInput 引用 inputs 表；组件 ActuatorReadiness/AimGate 可从实体配置注入实况。
- **门控统一入口**：决策就绪检查走 PassesActuatorGates，不过则带 UtilityAiReadinessBlockReason 可 trace。
- **诚实边界**：stance 现状编译保留但无系统消费——文档明示半成品（I6）。

## 3. 运行行为

就绪检查时读实体上的 ActuatorReadiness/AimGate 组件与执行器定义：aimGate 未就绪即拦（AimGateNotReady 等原因码）。stance 编译进 Stances 数组、profile 记 DefaultStanceId，但 UtilityAiStanceState 当前无系统读写。

## 4. 异常承诺

TargetFilter/ReadinessInput/AimGateInput/DefaultStance 引用未定义、DefaultStanceId 数字写法——启动失败并带路径。

**相关文档**：[配置说明](../config/ai-08-stances-actuators.md) · [ai-06](ai-05-dm-profiles.md) · [ai-03](ai-02-inputs.md)
