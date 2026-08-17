# fx-02 runtime spec · 效果模板骨架

> 引擎实现任务书。第一性需求见 [fx-02 PRD](../prd/fx-02-template.md)；现状见 [reference](../reference/fx-02-template.md)。

## 1. 概述

效果模板的解析、注册、冻结与热替换白名单合同。

## 2. 设计

- 顶层校验保持：id 逐字一致、tags ≤1、presetType 显式并按注册表→内建序解析、lifetime 精确三值、participatesInResponse 显式布尔；duration 拒标量；禁用字段（顶层 period、lifecycleDeploy）报错并指路；跨字段——modifiers 容量、Instant 禁 phaseListeners、六块"只在对应 presetType 合法且必须带"。
- 注册表保持：Finalize 后拒绝注册、重复 id 报冲突；FinalizeExecutionPlans 要求四窗口全 finalized。
- 热替换白名单保持：时长与周期、modifiers[0].value（下标与路径写法等价）、弹道效果引用（仅 LaunchProjectile 三引用位）、槽 0 固定授予 tag；失败保持原子，模板不被半改。

## 3. 精确语义与不变量

- 热替换只改数值不改身份，白名单外一律拒绝。

## 4. 迁移与治理

现状即基线；facts 页暂未收录模板注册表容量，待生成脚本补录。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-02 PRD](../prd/fx-02-template.md) · [reference](../reference/fx-02-template.md)
