# misc-04 runtime spec · 实体信息档案

> 引擎实现任务书。第一性需求见 [misc-04 PRD](../prd/misc-04-entity-info.md)；现状见 [reference](../reference/misc-04-entity-info.md)。

## 1. 概述

insight_profiles 的加载、互斥匹配与 GAS/本地化对接合同；能力 mod 承载的加载归属。

## 2. 设计

- 加载合同保持：ArrayById、模板键互斥、token/属性/能力引用逐条解析、source/display 封闭。
- 对接合同保持：stats.source=attribute 按名解析 AttributeRegistry（未知抛错）；actions.ability 引用能力 id。
- **治理项（D5）**：表在引擎 config_catalog 声明、loader 在 EntityInfoPanelsMod 实现——域归属与加载时序依赖 mod 装载窗口。方向：loader 收编引擎侧（域归引擎），或目录条目标注属主；现状下文档必须写明"装能力 mod 才生效"。
- **治理项**：与 T3 联动——该目录条目在未装能力 mod 时同样无消费方对账。

## 3. 精确语义与不变量

- 模板键 → 档案是单射；匹配不到模板 = 无面板（非错误）。
- 数值条展示在渲染时读当前属性快照；常量条恒定。

## 4. 迁移与治理

现状即基线；D5 归属治理入 TODO（todo/domains.md）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[misc-04 PRD](../prd/misc-04-entity-info.md) · [reference](../reference/misc-04-entity-info.md)
