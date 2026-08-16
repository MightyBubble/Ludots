# ai-06 editor spec · 任务

> 编辑器实现任务书。编辑器需求见 [ai-06 UXD](../uxd/ai-07-tasks.md)；引擎侧见 [runtime spec](../spec-runtime/ai-07-tasks.md)。

## 1. 概述

任务面板实现：Kind 驱动表单、双引用互验、出口预览。

## 2. 设计

- **Kind 联动**：SubmitOrder 展开订单区；组合 Kind 折叠并挂 I5 警示文案（与 runtime spec 措辞同源）。
- **双引用互验**：Key/Id 同给时本地对注册表互验，错误文案与 loader 一致。
- **出口预览**：复用 Order 构造的同源描述（I0/I1/Spatial 落位规则），只读渲染。
- **被引用索引**：与 ai-03 共用区间检查模块。

## 3. 精确语义与不变量

- 预览落位规则与 TrySubmitOrderTask 一致（含 IntArg0<0 时 I0 缺省）。
- 表单默认值（SubmitMode=0、PlayerId=0、IntArg0=-1、IntArg1=0、槽 -1）与 loader 相同。

## 4. 依赖接口与验收

- 消费：OrderTypeRegistry、AbilityDefinitionRegistry、tasks 合并视图、decisions 引用扫描。
- 验收：出口预览与运行 Order 一致；双写冲突被拦；组合 Kind 警示在保存前出现。

**相关文档**：[ai-06 UXD](../uxd/ai-07-tasks.md) · [ai-06 runtime spec](../spec-runtime/ai-07-tasks.md)
