# fx-21 editor spec · 出生下单

> 编辑器实现任务书。编辑器需求见 [fx-21 UXD](../uxd/fx-22-submit-order.md)；引擎侧见 [runtime spec](../spec-runtime/fx-22-submit-order.md)。

## 1. 概述

SubmitOrderFromBlackboard 效果表单的出生下单子表单：五键绑定、订单类型选择、提交模式。

## 2. 设计

- **五键绑定**：黑板键选择器绑定黑板键注册表；提供"推荐键族"分组（同前缀键聚合）降低五连选成本。
- **订单类型选择**：绑定订单类型表 key 集；两键独立选择，悬空阻保存。
- **链路提示**：检测本效果是否被造单位效果的 onSpawnEffect 引用（fx-15 交叉），展示链路示意。

## 3. 精确语义与不变量

- 落盘五键与两类订单 key 均已注册（校验与 loader 同源）。
- 槽位集合不含 None；缺省语义（不写即 Source/Target）在表单与落盘间往返无损。

## 4. 依赖接口与验收

- 消费：黑板键注册表、订单类型表、效果模板注册表（链路检测）、保存管线。
- 验收：五键任一悬空阻保存；与 fx-15 表单的出生链路提示正确显示。

**相关文档**：[fx-21 UXD](../uxd/fx-22-submit-order.md) · [fx-21 runtime spec](../spec-runtime/fx-22-submit-order.md)
