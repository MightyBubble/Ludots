# rt-04 editor spec · 表现事件

> 编辑器实现任务书。编辑器需求见 [rt-04 UXD](../uxd/rt-04-presentation.md)；引擎侧见 [runtime spec](../spec-runtime/rt-04-presentation.md)。

## 1. 概述

表现事件流查看器实现：缓冲投影、施法漏斗聚合、负载跳转。

## 2. 设计

- **流投影**：消费表现事件缓冲（引擎服务的只读视图），逐 tick 镜像；暂停停止消费并计数。
- **漏斗聚合**：编辑器侧对五种 Cast 事件做比例统计，失败原因按枚举七值分布——聚合不改原始序。
- **负载跳转**：AbilityId→技能定义面板、EffectTemplateId→效果模板面板（与 ed-01 目录树同源 id 解析）。
- **错误透传**：缓冲满错误原文透传并附容量配置直达链接。

## 3. 精确语义与不变量

- 条目字段与引擎负载结构一一对应，不增删字段。
- 聚合视图与流视图同源同帧，可交叉验证。

## 4. 依赖接口与验收

- 消费：表现事件缓冲只读视图、九种 Kind 与失败原因枚举、技能/效果注册表解析。
- 验收：一次施法全链路（Started→Committed→Finished+EffectApplied）在流中完整可见且序一致；过滤与跳转可用；容量错误透传无误。

**相关文档**：[rt-04 UXD](../uxd/rt-04-presentation.md) · [rt-04 runtime spec](../spec-runtime/rt-04-presentation.md)
