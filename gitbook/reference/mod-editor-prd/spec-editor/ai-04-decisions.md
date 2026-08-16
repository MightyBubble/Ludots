# ai-03 editor spec · 决策

> 编辑器实现任务书。编辑器需求见 [ai-03 UXD](../uxd/ai-04-decisions.md)；引擎侧见 [runtime spec](../spec-runtime/ai-04-decisions.md)。

## 1. 概述

决策面板实现：考量表编辑、节流表单、任务区间检查、分数预演。

## 2. 设计

- **考量表**：三引用列下拉直连 inputs/normalizations/curves 合并视图；Aggregate 枚举收窄。
- **区间检查**：编辑期模拟编译槽位分配（按合并视图顺序），预判 Tasks 连续性并在断点标红（I3 前移）。
- **分数预演**：复用运行时聚合公式实现（同 ai-02 同源策略），样例 raw 可拖动。
- **回退链显示**：task→decision→TryFindAbility 的槽位/技能回退链可视化标注当前生效源。

## 3. 精确语义与不变量

- 预演公式与 EvaluateDecision/ComputePriorityBucket 同源。
- 区间检查结果与 ResolveTaskRange 判定一致（同一连续性定义）。
- Flags 布尔与数组写法落盘前归一（单写法）。

## 4. 依赖接口与验收

- 消费：inputs/normalizations/curves/target_filters/tasks 五表合并视图、Ability/Tag 注册表、UtilityAiDecisionTrace（可选实时分）。
- 验收：三种聚合在预演中数值正确；不连续任务在保存前被拦截；Veto 短路可视。

**相关文档**：[ai-03 UXD](../uxd/ai-04-decisions.md) · [ai-03 runtime spec](../spec-runtime/ai-04-decisions.md)
