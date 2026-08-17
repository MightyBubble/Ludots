# input-01 runtime spec · 命令意图档案

> 引擎实现任务书。第一性需求见 [input-01 PRD](../prd/input-01-command-intent.md)；现状见 [reference](../reference/input-01-command-intent.md)。

## 1. 概述
意图路由合同：规则梯裁决、双侧条件、双路由终点、帧级意图解析链。

## 2. 设计
- 解析链保持：交互帧显式意图 > 控制方案默认 > 0 不路由；仲裁器只输出意图 id，不耦合映射系统。
- 规则裁决保持：priority 降序、命中即止、逐演员独立（independent 编组）；新编组策略经 mod 代码注册，不进 JSON。
- 引用校验保持：orderTypeKey 与槽位来源（`byAbilityTag:`/`contextGroup:` 前缀）加载期解析，失败即启动失败。
- 安装保持：知识门（KnowledgeCommandTargetGate）随档案安装，目标条件读知识投影。

## 3. 精确语义与不变量
- 同一演员同一帧至多一条路由胜出。
- 意图 id 为 0 时任何命令不路由——不路由必须可观察（诊断计数）。

## 4. 迁移与治理
现状即基线，无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[input-01 PRD](../prd/input-01-command-intent.md) · [reference](../reference/input-01-command-intent.md)
