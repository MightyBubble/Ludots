# fx-23 editor spec · 进度完成

> 编辑器实现任务书。编辑器需求见 [fx-23 UXD](../uxd/fx-21-progression.md)；引擎侧见 [runtime spec](../spec-runtime/fx-21-progression.md)。

## 1. 概述

CompleteProgression 效果表单的进度完成子表单：进度选择与阶梯透视、作用域三选、变更互斥。

## 2. 设计

- **进度选择器**：绑定进度注册表；选中条目投影等级阶梯摘要（只读）。
- **作用域控件**：self/explicit 固定项 + 命名作用域注册表动态项；内联说明各宿主来源。
- **变更单选组**：完成/设级/推进三态，level/delta 输入随选择启用，互斥由控件结构保证。

## 3. 精确语义与不变量

- 落盘组合与 loader 合法集一致：id 已注册、scope 三态之一、level/delta 至多其一且为正。
- 阶梯摘要与进度表内容同源（保存时刷新）。

## 4. 依赖接口与验收

- 消费：进度注册表、作用域注册表、进度表投影、保存管线。
- 验收：导入 level+delta 同写数据被校验面板拦截；命名作用域悬空阻保存。

**相关文档**：[fx-23 UXD](../uxd/fx-21-progression.md) · [fx-23 runtime spec](../spec-runtime/fx-21-progression.md)
