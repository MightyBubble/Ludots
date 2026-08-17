# misc-03 runtime spec · 叙事与任务

> 引擎实现任务书。第一性需求见 [misc-03 PRD](../prd/misc-03-narrative.md)；现状见 [reference](../reference/misc-03-narrative.md)。

## 1. 概述

叙事三表 + 任务表的加载、驱动与交叉引用合同：变量存储、对话推进、过场播放、任务信号。

## 2. 设计

- 加载合同保持：四表 ArrayById 依序注册；对话节点图、过场步骤、任务阶段的引用在加载期解析。
- 驱动合同保持：NarrativeDirector/RuntimeSystem 求推进（选项条件→动作→后继）；QuestRuntimeService 接收信号推进阶段。
- **治理项**：相机 id（dialogues/cinematics 引用 infra-03 预设）无启动期对账——加载后对 cameraId 做一次解析校验，未注册即抛（与 infra-03 治理项同项）。
- **治理项**：根表空占位（D3）与 T3 消费对账联动。

## 3. 精确语义与不变量

- 条件 5 种、动作 11 种封闭集合；节点后继必须落在同对话节点集内。
- 变量读写强类型：kind 与默认值字段绑定。
- 任务阶段推进 = requiredSignals 全齐；进入副作用（台词/过场）至多一次。

## 4. 迁移与治理

现状即基线；相机对账与 D3 入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[misc-03 PRD](../prd/misc-03-narrative.md) · [reference](../reference/misc-03-narrative.md)
