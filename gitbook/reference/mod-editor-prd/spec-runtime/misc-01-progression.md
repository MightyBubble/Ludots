# misc-01 runtime spec · 进度域

> 引擎实现任务书。第一性需求见 [misc-01 PRD](../prd/misc-01-progression.md)；现状见 [reference](../reference/misc-01-progression.md)。

## 1. 概述

进度三表（scope/progression/requirement）的加载、求值与 GAS CompleteProgression 对接合同。

## 2. 设计

- 加载合同保持：三表 ArrayById；scope 的 EntityCollection 引用在加载期对集合配置对账；条件树 kind 与参数严格校验。
- 效果对接合同保持：CompleteProgression 预设强制 Instant + progression 块；progression.id 在效果模板加载期经 ProgressionIdRegistry 解析，未注册抛错；id 上限与可冻结语义保持。
- **治理项**：progression id 注册上限为源码常量——入 facts 生成范围（数值纪律），编辑器用量预警数据源同此。

## 3. 精确语义与不变量

- 同一 scope 的成员集合在绑定系统内单一事实；需求求值只读。
- 条件树求值为纯函数：同一世界状态 + 同一树 → 同一结果。
- 冻结后再注册新 progression id 即失败。

## 4. 迁移与治理

现状即基线；上限入事实页入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[misc-01 PRD](../prd/misc-01-progression.md) · [reference](../reference/misc-01-progression.md)
