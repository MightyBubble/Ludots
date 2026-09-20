# map-01 runtime spec · 地图定义

> 引擎实现任务书。第一性需求见 [map-01 PRD](../prd/map-01-definition.md)；配置说明见 [map-01 配置说明](../config/map-01-definition.md)；现状见 [reference](../reference/map-01-definition.md)。

## 1. 概述

地图资产管线合同：片段收集 → 地图专属合并 → 继承解析 → 布阵实例化 → 棋盘与触发器装配。

## 2. 设计

- 合并语义不变量：Entities/Teams/Players 追加；Boards 按名覆盖；TriggerTypes 并集；相机后到者赢。
- **治理设计**：Entities 支持按 InstanceId 深合并覆盖（现状仅追加）——让难度修正能改既有实例的覆盖值而非只能加新实体；语义变更须附迁移说明。

## 3. 精确语义与不变量

- 片段收集顺序与配置管线一致（引擎默认 → 各 mod 按计划顺序）。
- 实例化顺序：先模板组件、后实例覆盖（逐组件写入）。
- 加载失败必须指明片段来源与条目。

## 4. 迁移与治理

现状即基线；InstanceId 覆盖合并为唯一新增设计项，随难度修正场景立项。

## 变更记录

- v1（2026-08-16）：初版。

**相关文档**：[map-01 PRD](../prd/map-01-definition.md) · [reference](../reference/map-01-definition.md)
