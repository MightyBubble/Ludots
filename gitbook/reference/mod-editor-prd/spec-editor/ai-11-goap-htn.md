# ai-11 editor spec · GOAP 与 HTN 规划

> 编辑器实现任务书。编辑器需求见 [ai-11 UXD](../uxd/ai-11-goap-htn.md)；引擎侧见 [runtime spec](../spec-runtime/ai-11-goap-htn.md)。

## 1. 概述

规划面板实现：六表资产树、手段网络图、位视图、计划预演。

## 2. 设计

- **资产树**：六表合并视图统一入口；htn_domain 以四数组结构视图呈现（DeepObject 无条目 id，靠数组位）。
- **atom 单一真源**：全部 atom 引用控件从 atoms 注册表取值，未声明即红条+快捷声明。
- **互斥表单**：投影 Op 驱动 IntKey/EntityKey 两组互斥；OrderTagId 字段不生成（对齐显式拒）。
- **手段网络**：goap_goals/goap_actions/htn Roots-Subtasks 建图；Pre/Post 位编辑器点选 atom。
- **计划预演**：调规划器 dry 接口（与运行引擎同实现），输出步序/代价/不可满足位。

## 3. 精确语义与不变量

- 位视图槽位与 AtomRegistry 分配一致。
- 预演结果与 GoapAStarPlanner256/HtnPlanner256 实际产出同源。

## 4. 依赖接口与验收

- 消费：六表合并视图、AtomRegistry、OrderTypeRegistry（黑板键域）、规划器 dry 接口。
- 验收：未声明 atom 全表拦截；预演与运行计划一致；htn_domain 四数组可编辑且引用越界被拦。

**相关文档**：[ai-11 UXD](../uxd/ai-11-goap-htn.md) · [ai-11 runtime spec](../spec-runtime/ai-11-goap-htn.md)
