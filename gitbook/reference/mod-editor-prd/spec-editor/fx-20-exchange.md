# fx-23 editor spec · 兑换

> 编辑器实现任务书。编辑器需求见 [fx-23 UXD](../uxd/fx-20-exchange.md)；引擎侧见 [runtime spec](../spec-runtime/fx-20-exchange.md)。

## 1. 概述

Exchange 效果表单的兑换子表单：操作选择与摘要透视、作用域、可执行性警示。

## 2. 设计

- **操作选择器**：绑定兑换操作注册表；选中条目投影输入/输出摘要（只读），提供跳转操作表的深链。
- **参数落盘**：选择即写 `_ep.exchangeOperationId`（type ExchangeOperation），作用域同理——与 fx-17 参数表同管线。
- **可执行性警示**：消费计划编译器认证集合（同源判定）出示常驻警示。

## 3. 精确语义与不变量

- 落盘参数的键与类型与 loader 要求一致（必填 id、可选 scope）。
- 摘要与操作表条目一致（同源投影，保存时刷新）。

## 4. 依赖接口与验收

- 消费：兑换操作注册表、作用域注册表、计划编译认证结果、保存管线。
- 验收：选未注册操作不可保存；摘要与操作表内容一致；警示条在区块启用即出现。

**相关文档**：[fx-23 UXD](../uxd/fx-20-exchange.md) · [fx-23 runtime spec](../spec-runtime/fx-20-exchange.md)
