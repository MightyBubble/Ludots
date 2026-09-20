# ab-10 runtime spec · 上下文组

> 引擎实现任务书。第一性需求见 [ab-10 PRD](../prd/ab-10-context-groups.md)；现状见 [reference](../reference/ab-10-context-groups.md)。

## 1. 概述

上下文组合同：加载期结构校验、打分消费链（空间查询→硬过滤→软打分→tie-break）。

## 2. 设计

- 加载保持：rootAbilityId 必填已知、searchRadiusCm 必填非负、candidates 非空；候选 abilityId/basePriority/requiresTarget 必填、两图可选但须可解析；requiresTarget=true 时距离/角度/悬停件全必填，false 缺省 0。
- 打分消费保持：根槽解析（I0=根槽）→SearchRadius 空间查询+视知门→逐候选 basePriority 起步、距离与角度硬过滤加权、悬停加成、scoreGraph 累加、preconditionGraph 出局；平分先比实体 Id 再比槽号。
- **治理项 AB7**：候选图 kind 要求（Validation/Score）运行期消费才校验——前移到加载期（注册表已含 kind），错 kind 启动即拒。

## 3. 精确语义与不变量

- 打分纯函数：同一世界状态恒得同一排序；前置图失败的候选出局不中断其余。

## 4. 迁移与治理
现状即基线；AB7 落地后回写。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-10 PRD](../prd/ab-10-context-groups.md) · [reference](../reference/ab-10-context-groups.md)
