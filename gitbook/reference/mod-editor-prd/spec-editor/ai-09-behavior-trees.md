# ai-11 editor spec · 行为树

> 编辑器实现任务书。编辑器需求见 [ai-10 UXD](../uxd/ai-09-behavior-trees.md)；引擎侧见 [runtime spec](../spec-runtime/ai-09-behavior-trees.md)。

## 1. 概述

树面板实现：画布编辑器、结构校验前置、单步 Tick 调试。

## 2. 设计

- **画布**：JSON 扁平结构 ↔ 图结构双向映射；拖拽产生 children 数组变更，序列化保持节点出现序。
- **结构校验**：多父/不可达/重复 id/root 缺失在编辑期用与 PackTree 同一判定逻辑（同源模块）。
- **枚举严格性**：下拉值固定枚举拼写，杜绝手输大小写错（I2 编辑器侧兜底）。
- **action 选择器**：GraphActionCatalog 过滤 host=BehaviorTree。
- **调试叠层**：接 BehaviorTreeWorld 的 per-agent 状态与 ThinkStats；Tick 手动驱动走 Restart/TickAll 同一入口。

## 3. 精确语义与不变量

- 画布校验结果与 PackTree 拒绝条件一致（同一不变量）。
- 落盘 JSON 与 GraphBehaviorDefinitionLoader 解析字段一一对应（id/root/nodes/kind/children/leaf/action）。

## 4. 依赖接口与验收

- 消费：behavior_trees 合并视图、schema 文件（结构提示）、GraphActionCatalog、BehaviorTreeWorld 运行态接口。
- 验收：结构非法无法保存；action 断链编辑期可见；Tick 单步能看到节点三态与 Running 游标续跑。

**相关文档**：[ai-10 UXD](../uxd/ai-09-behavior-trees.md) · [ai-10 runtime spec](../spec-runtime/ai-09-behavior-trees.md)
