# gr-op-10 editor spec · 节点：效果与事件动作

> 编辑器实现任务书。编辑器需求见 [gr-op-10 UXD](../uxd/gr-op-10-effect-actions.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-10-effect-actions.md)。

## 1. 概述

动作族目录与四个符号选择器从各注册表投影生成；保留通道警示与预算投影为编辑器侧派生。

## 2. 设计

- **目录条目**：描述符表扫描九行；非 Effect 图整组隐藏。
- **符号选择器**：模板/预设/事件/属性四个选择器接各注册表投影；写回节点字符串字段，不内联 id。
- **保留通道警示**：a/b 引脚角色静态映射；首次连线弹说明，可记住选择。
- **预算投影**：静态分析链上 TargetList 的来源（形状查询+过滤+截断），与上限（事实页）比对出预计档位；不可静态估计时不显示。

## 3. 精确语义与不变量

- 选择器候选与注册表投影一致；预算投影只提示不阻断。
- 非 Effect 图隐藏判定与掩码同源。

## 4. 依赖接口与验收

- 消费：描述符表、效果/派发预设/事件 tag/属性注册表投影、链静态分析接口。
- 验收：非 Effect 图整组隐藏；模板选择器显示 presetType；a/b 首连弹说明。

**相关文档**：[gr-op-10 UXD](../uxd/gr-op-10-effect-actions.md) · [gr-op-10 runtime spec](../spec-runtime/gr-op-10-effect-actions.md)
