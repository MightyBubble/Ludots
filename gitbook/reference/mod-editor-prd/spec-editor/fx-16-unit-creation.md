# fx-15 editor spec · 造单位

> 编辑器实现任务书。编辑器需求见 [fx-15 UXD](../uxd/fx-16-unit-creation.md)；引擎侧见 [runtime spec](../spec-runtime/fx-16-unit-creation.md)。

## 1. 概述

CreateUnit 效果表单的造单位子表单：来源互斥、图案联动、归属开关持久化。

## 2. 设计

- **来源切换**：templateId/unitType 两组视图模型互斥保存；未选侧的字段不落盘。
- **图案联动**：pattern 决定摆放字段可见集与必填集，规则与 loader 的 Require/Absent 判定同源；归属开关双态（true/无字段），false 永不落盘。
- **引用选择**：模板与出生效果走各自注册表，悬空阻保存。

## 3. 精确语义与不变量

- 表单落盘字段集 ⊆ 所选图案的合法集；往返保存无损。
- count、半径类数值的校验区间与 loader 一致。

## 4. 依赖接口与验收

- 消费：实体模板注册表、unitType 注册表、效果模板注册表、保存管线。
- 验收：Circle 缺起始角保存被拒；归属开关关闭后 JSON 中无该字段。

**相关文档**：[fx-15 UXD](../uxd/fx-16-unit-creation.md) · [fx-15 runtime spec](../spec-runtime/fx-16-unit-creation.md)
