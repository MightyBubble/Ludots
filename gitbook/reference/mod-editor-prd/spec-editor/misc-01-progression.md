# misc-01 editor spec · 进度域

> 编辑器实现任务书。编辑器需求见 [misc-01 UXD](../uxd/misc-01-progression.md)；引擎侧见 [runtime spec](../spec-runtime/misc-01-progression.md)。

## 1. 概述

进度编辑器实现：范围/进度线/条件树三栏与 GAS 挂钩检查。

## 2. 设计

- **三栏模型**：三表投影 + 引用联动（scope→progression→requirement）。
- **条件树编辑器**：kind 参数化表单（schema 驱动，kind 集合来自引擎枚举投影）；树深度按引擎合同限制。
- **挂钩检查**：扫描效果表的 CompleteProgression 预设，建立进度线↔效果双向索引；孤儿清单驱动提示。
- **守卫**：集合/scope/tag 引用下拉封闭；上限用量条数据源同 facts。

## 3. 精确语义与不变量

- 条件树表单序列化与手写 JSON 等价；求值预览与 RequirementEvaluator 同判定。
- 挂钩索引与效果表实时一致。

## 4. 依赖接口与验收

- 消费：三表投影、集合配置、tag 注册表、效果表扫描接口。
- 验收：新建进度线 + 挂效果产物通过启动校验；孤儿检查无漏报。

**相关文档**：[misc-01 UXD](../uxd/misc-01-progression.md) · [misc-01 runtime spec](../spec-runtime/misc-01-progression.md)
