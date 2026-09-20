# rt-04 runtime spec · 表现事件

> 引擎实现任务书。第一性需求见 [rt-04 PRD](../prd/rt-04-presentation.md)；现状见 [reference](../reference/rt-04-presentation.md)。

## 1. 概述

逻辑→表现单向回执合同：九种事件、结构化失败原因、每 tick 定容缓冲、溢出即配置错误、事务撤销。

## 2. 设计

- 九种 Kind 与七值失败原因枚举保持；负载字段集保持（Kind/Actor/Target/AbilitySlot/AbilityId/EffectTemplateId/AttributeId/Delta/FailReason）。
- 缓冲合同保持：容量构造校验必 >0、Publish 满抛（overflow is a configuration error）、事务写检查点回滚。
- 消费时序保持：逻辑 tick 写入 → 表现投影系统在 ClearPresentationFlags 组消费并清标志——投影先于清标志。
- 容量来源保持 game.json presentation 块，核心 mod 基线供默认。

## 3. 精确语义与不变量

- 每事件四类身份字段（施法者/目标/槽/定义 id）齐全——消费方不回查逻辑态。
- 回滚后缓冲内容与事务前一致；表现层永不见已回滚事件。
- 溢出处理唯一：抛错。禁止改为丢弃+计数（那是预算通道 rt-02 的语义，两通道不混）。

## 4. 迁移与治理

现状即基线，无新增设计项。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[rt-04 PRD](../prd/rt-04-presentation.md) · [reference](../reference/rt-04-presentation.md)
