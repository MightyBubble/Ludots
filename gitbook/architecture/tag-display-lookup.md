# （已废止）Tag → 展示 Token 专线查表

> **状态：SUPERSEDED / 废止**  
> **生效正本**：[`graph-table-lookup.md`](graph-table-lookup.md)（通用查表是**唯一**查表路径）  
> **Issue SSOT**：全景 [#886](https://github.com/MightyBubble/Ludots/issues/886) · ADR [#876](https://github.com/MightyBubble/Ludots/issues/876) · 实现 [#881](https://github.com/MightyBubble/Ludots/issues/881) · 清理 [#877](https://github.com/MightyBubble/Ludots/issues/877)  
> **产品裁定**：不得在 Core 维护「TagDisplay」专用映射表或专用查表 opcode。若 Mod 需要「状态标签 → 文案 token」，作者自建一张普通 lookup 表，走 `ResolveTableRow` + `TableRead*`。

## 为什么废止

先前设计把「互斥 State.* → 面板文案」做成与通用查表并列的框架专线（`TagDisplayTableRegistry`、`LookupTagDisplayToken`、`tag_display_tables.json`）。这违反：

- **数据驱动 / 用户自建表**：映射语义属于 Mod 内容，不是框架硬编码表种  
- **SSOT / DRY**：两条查表路径会让后人继续叠旁路  
- **禁止重复造轮子**：通用表足以覆盖该场景

## 迁移指引（给后续实现）

| 旧说法 | 新说法 |
|--------|--------|
| `LookupTagDisplayText` / `LookupTagDisplayToken` | 删除；改 `ResolveTableRow` + `TableReadInt(displayToken)` |
| `GraphTables/tag_display_tables.json` | 不采用；改通用 lookup 表资产 |
| `TagDisplayTableRegistry` 接入生产 | **禁止**；按清理 issue 删除 |
| 读当前状态 tag | 纯读玩法标签（tag id）→ 再查**作者表** |

历史长文论证已失效，不再作为实现依据。勿在本页追加新合同。
