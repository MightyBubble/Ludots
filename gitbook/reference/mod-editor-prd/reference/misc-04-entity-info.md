# misc-04 reference · 实体信息档案

> 现状参考。第一性需求见 [misc-04 PRD](../prd/misc-04-entity-info.md)；配置说明见 [misc-04 配置说明](../config/misc-04-entity-info.md)。

## 1. 现状快照

- 表 `EntityInfo/insight_profiles.json`：目录在引擎 config_catalog 声明（ArrayById、AllowEmpty）；**loader 实现在 mod 内**（EntityInfoPanelsMod/Insight，D5）。
- 字段：id、templateIds（互斥校验：跨档案重复抛错）、accentColorHex/surfaceColorHex、genreGlyph/portraitGlyph、genreLabelToken/subtitleToken/bodyToken（token 必须解析）、badges/stats/tips/actions。
- GAS 联动：stats.source=attribute 按名解析 AttributeRegistry，未知抛错；source 另有 constant；display 三值（current/currentOverBase/constant）；actions.ability 引用能力 id。
- 样例：GenreInfoShowcaseMod 三档案（4X/MOBA/RTS）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 目录声明 | assets/config_catalog.json:437 |
| 档案加载器（mod 内实现） | mods/capabilities/entityinfo/EntityInfoPanelsMod/Insight/EntityInsightProfileLoader.cs:13,31 |
| 模板互斥校验 | EntityInsightProfileLoader.cs:44-53 |
| stats 属性解析（未知抛） | EntityInsightProfileLoader.cs:162-168 |
| 样例 | mods/showcases/info_panels/GenreInfoShowcaseMod/assets/EntityInfo/insight_profiles.json |

**相关文档**：[misc-04 PRD](../prd/misc-04-entity-info.md) · [pres-04 reference](pres-04-localization.md)
