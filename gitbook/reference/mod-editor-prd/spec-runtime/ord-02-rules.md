# ord-02 runtime spec · 订单规则与打断

> 引擎实现任务书。第一性需求见 [ord-02 PRD](../prd/ord-02-rules.md)；现状见 [reference](../reference/ord-02-rules.md)。

## 1. 概述
规则裁决合同：阻止/打断两张快查表、同型与跨型两级打断判定、打断链原子化。

## 2. 设计
- 裁决顺序保持：排队态分派（`allowQueuedMode`/排队容量）→ 即时态三步（阻止表 → 打断判定 → 打断链或同型策略退化）。
- 打断链顺序保持：预写激活黑板 → 旧单终态化（Cancelled·Interrupted）→ 按需清队 → 激活提交；预写失败即整体未发生。
- 打断判定保持：无活动单可打断；同型看 `canInterruptSelf`；跨型查表，无边即不可打断。
- 加载校验保持：引用须已注册、单表去重、fixed 容量。

## 3. 精确语义与不变量
- 阻止判定只针对活动单类型；排队单不触发阻止。
- 打断链对外不可见中间态：终态化与激活在同一提交调用内完成。
- 拒收必须带规则原因码（RejectedByRule / RejectedQueueFull）。

## 4. 迁移与治理
现状即基线，无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ord-02 PRD](../prd/ord-02-rules.md) · [reference](../reference/ord-02-rules.md)
