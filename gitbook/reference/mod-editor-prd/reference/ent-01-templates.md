# ent-01 reference · 实体模板

> 现状参考。第一性需求见 [ent-01 PRD](../prd/ent-01-templates.md)；配置说明见 [ent-01 配置说明](../config/ent-01-templates.md)。

## 1. 现状快照

- 模板 = id + extends（可选继承，分类/单亲链通道）+ uses（可选组件组组装，推荐范式）+ onSpawnEffect + components（组件名→原始 JSON 的开放映射），经配置目录表加载（`Entities/templates.json`，数组按 id 合并）。块是普通模板条目，被 `uses` 引用即为块。
- 装载顺序：同 id 合并 → extends/uses 折叠（字段级深合并，跨 mod 可引用父模板/块）→ 校验；折叠优先级一条规则——声明越靠后优先级越高，自身 components 永远最高；同组件多源写入的覆盖链记入 `ConfigConflictReport`。展开后物化只消费合并结果。
- 实例化：地图布阵（Template + InstanceId + Overrides）与效果造单位共用装配路径；实例覆盖与模板组件字段级深合并。
- 出生效果在组件就绪后施放。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 模板形状（id/extends/uses/onSpawnEffect/components） | src/Core/Config/EntityTemplate.cs |
| extends/uses 装载期折叠器 | src/Core/Config/EntityTemplateInheritance.cs |
| 组件覆盖链报告（uses 折叠留痕） | src/Core/Config/ConfigConflictReport.cs（`RecordComponentOverrideChain`） |
| 目录登记（Entities/templates.json，数组按 id） | assets/config_catalog.json |
| 运行时折叠接入（合并后、校验前，传 report） | src/Core/Systems/MapLoader.cs（`LoadTemplates` → `ExpandTemplateInheritance`） |
| 离线烘焙侧同语义接入 | src/Core/Ludots.Physics2D/Navigation/NavObstacleAuthoringCatalog.cs |
| 装配与实例化（override 字段级深合并） | src/Core/Config/EntityBuilder.cs |
| 展开与合并语义测试 | src/Tests/GasTests/Config/EntityTemplateInheritanceTests.cs、src/Tests/GasTests/Config/EntityTemplateUsesTests.cs |
| 真实模板表 | mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Entities/templates.json |

**相关文档**：[ent-01 PRD](../prd/ent-01-templates.md) · [map-01 reference](map-01-definition.md)
