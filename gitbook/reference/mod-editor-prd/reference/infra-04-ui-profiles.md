# infra-04 reference · 界面档案

> 现状参考。第一性需求见 [infra-04 PRD](../prd/infra-04-ui-profiles.md)；配置说明见 [infra-04 配置说明](../config/infra-04-ui-profiles.md)。

## 1. 现状快照

- ability_aggregation_profiles：ArrayById；字段 id、groupBy；两条内建表达式 `aggregation.by_template`→`template.id`、`aggregation.by_ability_id`→`ability.id`；结构校验在加载器，前缀解析在 AbilityAggregationProfileRegistry 安装期。
- command_deck_profiles：DeepObject，根键 profiles；根表现状 `{"profiles":[]}` 空（D3）。
- production_overview_profiles：同构空根表（D3）。
- UI 域目录三表登记（见事实页 UI 计数）；面板内容无引擎默认，全部由 mod 下沉（现状无 mod 行）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 聚合档案加载 | src/Core/UI/EntityCommandPanels/AbilityAggregationProfileConfigLoader.cs:35 |
| 聚合安装与表达式解析 | src/Core/UI/EntityCommandPanels/AbilityAggregationProfileRegistry.cs:15 |
| 命令甲板加载 | src/Core/UI/CommandDeck/CommandDeckProfileConfigLoader.cs:32 |
| 生产总览合同 | src/Core/UI/ProductionOverview/ProductionOverviewContracts.cs:44 |
| 实配资产 | assets/UI/ability_aggregation_profiles.json、command_deck_profiles.json、production_overview_profiles.json |

**相关文档**：[infra-04 PRD](../prd/infra-04-ui-profiles.md) · [misc-04 reference](misc-04-entity-info.md)
