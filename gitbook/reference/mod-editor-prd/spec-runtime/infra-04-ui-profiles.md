# infra-04 runtime spec · 界面档案

> 引擎实现任务书。第一性需求见 [infra-04 PRD](../prd/infra-04-ui-profiles.md)；现状见 [reference](../reference/infra-04-ui-profiles.md)。

## 1. 概述

UI 三档案（技能聚合 / 命令甲板 / 生产总览）的加载、编译与消费合同。

## 2. 设计

- 加载合同保持：聚合表 ArrayById 结构校验，groupBy 前缀解析在注册表安装期（两遍式）；甲板/总览 DeepObject 根键 profiles。
- 编译合同保持：groupBy 仅内建表达式（by_template→template.id、by_ability_id→ability.id），不开放任意脚本。
- **治理项（D3）**：甲板/总览根表为空占位，全仓库无真实行——面板布局通道 latent。补 showcase 或在文档长期标注；与 T3（目录条目消费对账）联动确认两表确有消费方。
- **治理项**：档案缺失回退内建布局的现状语义写进错误口径：缺失非错误、结构错误才是。

## 3. 精确语义与不变量

- 聚合分组是纯函数：同一实体技能集 + 同一档案 → 同一分组。
- 表达式集合封闭；新增表达式 = 引擎发版，不是配置行为。

## 4. 迁移与治理

现状即基线；D3 showcase 入 TODO（todo/domains.md）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[infra-04 PRD](../prd/infra-04-ui-profiles.md) · [reference](../reference/infra-04-ui-profiles.md)
