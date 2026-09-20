# tag-03 editor spec · Tag 变化与事件

> 编辑器实现任务书。编辑器需求见 [tag-03 UXD](../uxd/tag-03-changed-events.md)；引擎侧见 [runtime spec](../spec-runtime/tag-03-changed-events.md)。

## 1. 概述

事件流查看器与反应绑定编辑的实现。

## 2. 设计

- **事件流投影**：订阅运行实例的事件与诊断通道，环形缓冲镜像（容量与引擎一致，取自事实页）；暂停即停止消费并计数。
- **条目负载**：事件 tag、实体、旧/新值、反应尝试结果——与引擎事件负载同字段。
- **反应绑定编辑**：写实体模板的 ReactionBuffer 组件（schema 表单，ent-01 同源）。

## 3. 精确语义与不变量

- 时间线序 = 引擎分发序（同源，不重排）。
- 离线时绑定编辑可用、查看器隐藏。

## 4. 依赖接口与验收

- 消费：事件订阅通道、诊断遥测、ReactionBuffer schema。
- 验收：注入变化后时间线条目与引擎日志一致；绑定改动落盘即生效于下次启动。

**相关文档**：[tag-03 UXD](../uxd/tag-03-changed-events.md) · [tag-03 runtime spec](../spec-runtime/tag-03-changed-events.md)
