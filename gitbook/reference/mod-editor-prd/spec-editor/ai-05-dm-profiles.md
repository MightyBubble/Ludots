# ai-05 editor spec · 决策者与档案

> 编辑器实现任务书。编辑器需求见 [ai-05 UXD](../uxd/ai-05-dm-profiles.md)；引擎侧见 [runtime spec](../spec-runtime/ai-05-dm-profiles.md)。

## 1. 概述

装配台实现：三层引用树、区间检查、节奏表单、挂接反查。

## 2. 设计

- **档案树**：profiles/decision_makers/decisions 三表合并视图构建；断链节点标红。
- **区间检查**：与 ai-04 同一连续性判定模块复用（同源不变量）。
- **实时竞技场**：订阅 UtilityAiDecisionTrace（无则占位）；分数/桶/状态字段与组件一一对应。
- **挂接反查**：扫描实体模板的 UtilityAiAgent.ProfileId，列出模板并支持跳转。
- **mode 联动**：SelectionMode 切换驱动 margin 控件可用性，落盘字段不变。

## 3. 精确语义与不变量

- 树的引用解析与 loader 字典同源（Ordinal）。
- 换挡规则提示文案与 runtime 的 score→bucket→distance 次序一致。

## 4. 依赖接口与验收

- 消费：三表合并视图、UtilityAiDecisionTrace、实体模板扫描、trace 工具连接。
- 验收：断链与不连续在编辑期可见；interval/maxCandidates 非正被拦截；FixedPriority 下 margin 不可编辑且不落盘歧义。

**相关文档**：[ai-05 UXD](../uxd/ai-05-dm-profiles.md) · [ai-05 runtime spec](../spec-runtime/ai-05-dm-profiles.md)
