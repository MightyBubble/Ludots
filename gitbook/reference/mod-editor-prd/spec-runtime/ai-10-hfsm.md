# ai-09 runtime spec · 层次状态机

> 引擎实现任务书。第一性需求见 [ai-09 PRD](../prd/ai-10-hfsm.md)；现状见 [reference](../reference/ai-10-hfsm.md)。

## 1. 概述

HFSM 加载与执行合同：层级结构校验、转移择优、LCA 生命周期、生命周期图预算。

## 2. 设计

- 解析合同保持：state kind/predicate ignoreCase:false、Compound defaultChild、Leaf 无 children、禁多父禁不可达、图名 Require(host=Hfsm)。
- 择优合同保持：当前叶向上逐层评估同 from 转移；priority 降序、**平级后定义者胜**（`tr.Priority >= bestPriority`）。
- 生命周期合同保持：切换沿 LCA ExitUpTo→EnterDownFrom；StimulusLatched 触发后清零；onTick 每波。
- 生命周期图执行保持：64 步预算禁 Yield、未 halt 报错；两指令+halt 快路径；程序缓存 8 条。
- 上限保持：MaxStates=64、MaxTransitions=128、MaxStackDepth=8（随 facts 再生）。
- **治理项（引 todo/ai.md）**：I8——平级后定义者胜与直觉相反，改严格大于（先定义者胜）属行为变更，先文档化+编辑器标注；I2/I10 同 ai-08。

## 3. 精确语义与不变量

- 转移评估自叶上爬：深层转移优先于祖先层。
- 同一时刻每 agent 恰处一个叶态；切换只经过 LCA 一次。
- StimulusLatched 语义：置位一次至多触发一条转移。

## 4. 迁移与治理

现状即基线；I8 文档化先行，行为变更需独立立项。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-09 PRD](../prd/ai-10-hfsm.md) · [reference](../reference/ai-10-hfsm.md)
