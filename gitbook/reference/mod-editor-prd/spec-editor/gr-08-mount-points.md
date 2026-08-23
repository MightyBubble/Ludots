# gr-08 editor spec · 挂接点总表

> 编辑器实现任务书。编辑器需求见 [gr-08 UXD](../uxd/gr-08-mount-points.md)；引擎侧见 [runtime spec](../spec-runtime/gr-08-mount-points.md)。

## 1. 概述

挂接导航实现：挂接点表投影、双向过滤、各域挂接编辑跳转。

## 2. 设计

- 挂接点表（挂点 × kind 合同 × 消费时机）由引擎挂点注册信息投影，编辑器不维护副本。
- 双向过滤：按图 kind 过滤可挂点、按挂点过滤可挂图，判定同源。
- 各域挂接编辑入口按域篇实现（fx/ab/ai 等），导航只负责跳转与上下文（当前图名）。

## 3. 精确语义与不变量

- 导航过滤结果与引擎 RequireKind 终检一致。
- 挂点清单与引擎实际消费点一一对应（新增挂点先登记后呈现）。

## 4. 依赖接口与验收

- 消费：挂接点表投影、图注册表、kind 合同。
- 验收：六 kind 各选一图，导航可挂集合与实测挂接成败一致；悬空挂接全量可见。

**相关文档**：[gr-08 UXD](../uxd/gr-08-mount-points.md) · [gr-08 runtime spec](../spec-runtime/gr-08-mount-points.md)
