# fx-23 editor spec · 生命周期原子操作

> 编辑器实现任务书。编辑器需求见 [fx-23 UXD](../uxd/fx-23-lifecycle-atomic.md)；引擎侧见 [runtime spec](../spec-runtime/fx-23-lifecycle-atomic.md)。

## 1. 概述

DeployConsumeSource 效果表单的部署子表单：模板与属性切片配置、六步链示意、可执行性警示。

## 2. 设计

- **参数表单**：复用 fx-17 参数区块的保留键视图（`_ep.targetEntityTemplate` 等），类型化控件按保留键约束收窄（模板选择器、Base/Current 单选、属性多选上限 4）。
- **链路示意**：六步静态序列组件，标注回滚边界；随执行器 op 序列常量同源生成，防漂移。
- **可执行性警示**：消费计划编译器认证集合（同源判定）出示常驻警示，不硬拦保存。

## 3. 精确语义与不变量

- 落盘参数三件套与 loader 必配检查一一对应；属性键数 1..容量上限（常量与事实页同源）。
- 链路示意步骤与执行器 op 序列一致。

## 4. 依赖接口与验收

- 消费：实体模板注册表、属性注册表、fx-17 参数管线、计划编译认证结果、保存管线。
- 验收：属性键为空阻保存；勾选第 5 项被禁用；警示条在表单启用即出现。

**相关文档**：[fx-23 UXD](../uxd/fx-23-lifecycle-atomic.md) · [fx-23 runtime spec](../spec-runtime/fx-23-lifecycle-atomic.md)
