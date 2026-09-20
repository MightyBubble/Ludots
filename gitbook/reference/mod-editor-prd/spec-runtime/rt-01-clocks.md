# rt-01 runtime spec · 时钟系统

> 引擎实现任务书。第一性需求见 [rt-01 PRD](../prd/rt-01-clocks.md)；现状见 [reference](../reference/rt-01-clocks.md)。

## 1. 概述

三域时间合同：全局固定帧+步进双域、每实体本地步进、千分比步进策略与 fail-fast 校验。

## 2. 设计

- 时钟域合同保持：GAS 侧 GasClockId 三值，其中仅固定帧与步进映射为引擎全局域；引擎另有物理/导航两个全局域（GAS 不消费）。实体本地不是时钟域——是组件上的步数累加器。
- 步进策略保持三态与千分比累进消费（PermilleStepAccumulator）；scalePermille ≥0 无上限、默认 1000；RequestStep 仅 Manual 语义（其他态下挂起不消费）。TurnAdvanced 仅随手动步发射——保持，写入脚本事件合同。
- 实体本地五连校验保持（存在/有限/整数千分比 ±0.001/≥0/≤上限）；缺 AttributeBuffer 抛错。
- 步进速率换算（FixedHz÷max(1,stepEveryFixedTicks)）保持启动期一次注入；消费方按 RequirePositive 校验。

## 3. 精确语义与不变量

- 每固定 tick 恰好一次固定帧推进；步进消费 0..N 由策略决定。
- 本地步数单调不减；本地消费输入=已消费全局步×本地千分比，阈值 1000 累加。
- 固定帧先于步进；同一 tick 内步进次数不因 scalePermille 突变而超过策略上限。
- 校验失败一律抛错带实体/属性上下文——禁止夹取。

## 4. 迁移与治理

现状即基线，无新增设计项。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[rt-01 PRD](../prd/rt-01-clocks.md) · [reference](../reference/rt-01-clocks.md)
