# infra-02 runtime spec · 导航配置

> 引擎实现任务书。第一性需求见 [infra-02 PRD](../prd/infra-02-navigation.md)；现状见 [reference](../reference/infra-02-navigation.md)。

## 1. 概述

导航三表（体型档案 / 寻路 agent 类型 / 网格烘焙）的加载、校验与烘焙管线合同。

## 2. 设计

- 加载合同保持：档案 ArrayById 且至少一条；pathing 根对象封闭（agentTypes 唯一根键）非空；navmesh DeepObject。
- 选路合同保持：mode 与双权重决定网格/图路径选择；areaCosts 作用于网格代价；nodeGraph 投影规则含动态覆盖开关。
- 烘焙合同保持：offline + recast；runtimeIncremental 按固定步预算分摊瓦片重建。
- **治理项**：draftCm/beamCm 面向载具类体型的语义在当前 showcase 无消费样例——补一个载具导航 showcase 或在配置说明长期标注"预留"。

## 3. 精确语义与不变量

- agent 类型的 profileId 必须指向已注册档案。
- 面积代价引用必须落在 navmesh areas 声明集内。
- 增量重建每固定步瓦片数 ≤ tileBudgetPerFixedTick。

## 4. 迁移与治理

现状即基线；载具样例治理入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[infra-02 PRD](../prd/infra-02-navigation.md) · [reference](../reference/infra-02-navigation.md)
