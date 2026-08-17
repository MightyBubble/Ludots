# ai-11 editor spec · 层次状态机

> 编辑器实现任务书。编辑器需求见 [ai-11 UXD](../uxd/ai-10-hfsm.md)；引擎侧见 [runtime spec](../spec-runtime/ai-10-hfsm.md)。

## 1. 概述

HFSM 面板实现：层级画布、转移排序与平局标注、Stimulus/生命周期调试。

## 2. 设计

- **层级画布**：states 树渲染 + children 编辑；结构校验与 loader 同判定（defaultChild/多父/不可达）。
- **平局标注**：同 from 同 priority 的转移按声明序计算实际胜者并前置提示（I8 编辑器侧补偿）。
- **枚举严格性**：下拉固定拼写，杜绝手输大小写错。
- **图选择器**：GraphActionCatalog 过滤 host=Hfsm，覆盖 onEnter/onTick/onExit/condition 四槽。
- **调试**：接 HfsmWorld（当前叶、LatchStimulus、HfsmThinkStats），单步可视化 LCA 收展。

## 3. 精确语义与不变量

- 平局胜者计算与 `Priority >= bestPriority` 后定义胜规则同源。
- 落盘字段与 GraphBehaviorDefinitionLoader 解析名一一对应（states/transitions 全字段）。

## 4. 依赖接口与验收

- 消费：hfsm 合并视图、schema（结构提示）、GraphActionCatalog、HfsmWorld 运行态接口。
- 验收：结构非法禁存；平局胜者编辑期标注；单步可观察 StimulusLatched 触发与清零。

**相关文档**：[ai-11 UXD](../uxd/ai-10-hfsm.md) · [ai-11 runtime spec](../spec-runtime/ai-10-hfsm.md)
