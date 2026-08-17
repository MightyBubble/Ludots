# fx-15 editor spec · 目标派发

> 编辑器实现任务书。编辑器需求见 [fx-14 UXD](../uxd/fx-11-target-dispatch.md)；引擎侧见 [runtime spec](../spec-runtime/fx-11-target-dispatch.md)。

## 1. 概述

派发映射编辑器：三槽图、预设填充、互斥与引用校验、扇出预估。

## 2. 设计

- 三槽模型直接映射 contextMapping；预设选中时以只读视图呈现并标注等价性。
- 载荷效果选择器消费效果注册表；被引用关系做反向索引。
- 扇出预估复用 fx-12 预览的候选计数，不重算。

## 3. 精确语义与不变量

- 表单三槽 ⇔ contextMapping 往返无损；默认态不落任何映射字段。
- 互斥判定与 loader 同源。

## 4. 依赖接口与验收

- 消费：派发预设表、效果注册表、过滤候选计数。
- 验收：预设/显式/默认三态切换往返无损；载荷悬空在删除方即可见。

**相关文档**：[fx-14 UXD](../uxd/fx-11-target-dispatch.md) · [fx-14 runtime spec](../spec-runtime/fx-11-target-dispatch.md)
