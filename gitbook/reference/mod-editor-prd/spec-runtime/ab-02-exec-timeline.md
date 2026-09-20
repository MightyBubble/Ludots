# ab-02 runtime spec · 执行时间轴

> 引擎实现任务书。第一性需求见 [ab-02 PRD](../prd/ab-02-exec-timeline.md)；现状见 [reference](../reference/ab-02-exec-timeline.md)。

## 1. 概述

时间轴执行合同：两阶段推进、黑板接缝、Gate 等待、打断、终态映射、容量预检。

## 2. 设计

- 结构保持 SoA 定长（8 组列 × 上限 16），编译期全解析、运行期零分配；起播黑板读槽位（缺失→MissingBlackboardSlot）、目标实体/位置（多点首点原点、末点目标）；步进 CurrentTick=ClockNow−StartAbsoluteTick，按 NextItemIndex 消费到期条目，End/耗尽→Finished。
- 容量/Gate/打断：到期效果先过队列容量预检（不足→SubmissionQueueFull）；InputGate/TargetCollectionGate 入请求队列（满→失败），EventGate 记等待 tag+截止、超时放行；interruptAny 命中→Interrupted、订单替换仅发打断事件；终态映射 Completed/Cancelled/Failed，原因缺失即抛错。
- **治理项 AB1**：TagSignal 增/删语义藏于 payloadA 整数、JSON 无枚举名——加载器增加具名写法并移除裸整数路径。**AB2**：Committed 为死枚举值——移除或接通。

## 3. 精确语义与不变量

- 同帧完成后再扫描承接续单，上限 4 轮；Gate 挂起期不推进后续条目，挂起的进度需求遇非 Gate 条目即失败（ab-05）。

## 4. 迁移与治理
现状即基线；AB1/AB2 落地后回写 reference。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-02 PRD](../prd/ab-02-exec-timeline.md) · [reference](../reference/ab-02-exec-timeline.md)
