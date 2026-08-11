## GAS Composition Gate — Self Review

- **Task / Issue**: #881 feat(graph): 通用查表 ResolveTableRow + TableReadInt/TableReadFloat
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新增 L0 graph ops + 只读表 Registry/Loader）

结论: **PASS**

一句话理由: 查表是单一职责原子读 op；表内容由 Mod 资产表达，不新增 profile enum / preset 开关 / 平行玩法管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| ResolveTableRow | 0 | GraphNodeOp + Pure handler |
| TableReadInt | 0 | GraphNodeOp + Pure handler |
| TableReadFloat | 0 | GraphNodeOp + Pure handler |
| GraphLookupTableRegistry | 0 只读索引 | StringIntRegistry 组合 |
| GraphLookupTableLoader | 配置装载 | ConfigPipeline ArrayById |
| 作者表语义（rank/state_display…） | 3（内容） | Mod JSON，非 Core enum |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` Pure 分类与注册模式
- Queues / Systems: 无新 System；沿用既有 Graph VM 执行
- Resolvers / Registries: `StringIntRegistry`、`ConfigPipeline`、`IGraphSymbolResolver` / `GraphProgramSymbolPatcher`、`IGraphRuntimeApi` / `GasGraphRuntimeApi`、`PresentationTextCatalog`（TextToken→id）
- Existing presets / graphs: 不改 EffectPreset；作者用 Query/Derived 图连线组合

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ResolveTableRow | key → rowHandle | 现有 op 无通用表索引；禁止复活 TagDisplay 专线 |
| TableReadInt | rowHandle+field → Int/TextToken id | 需 typed 只读字段访问 |
| TableReadFloat | rowHandle+field → Float | 同上 |

### 5. Transaction boundary

必须原子 rollback 的步骤: **无**（纯读，无 ECS 写 / 无 effect 事务）

### 6. Config SSOT

行为配置落在: **graph**（ops 连线）+ Mod lookup 表资产 `GraphTables/lookup_tables.json`

是否新增 JSON schema: **YES** — 通用 lookup 表是数据内容合同，不是 profile DSL；列语义由 Mod 定义，框架只提供 typed 列 kind（Int/Float/TextToken）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺表/缺行/缺字段/类型不符 fail-closed）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换表 id / 字段 / key 来源）

若选了 Core enum → FAIL

---

## Appendix: #858 / #875 面板投影落地审计（#883）

> **不覆盖上文 #861 主体。** 面板线审计正本（可提交）：`docs/audits/875-or-858-audit-handoff.md`  
> 约定路径 `artifacts/pr/875-or-858-audit-handoff.md` 因 `artifacts/` gitignore 不可作为唯一交付。

| 项 | 结论 |
|----|------|
| 资源条 MVP + PanelProjectionReader + 编辑器样板 | **已做**（#875 / main） |
| UIP-0 Template/Instance/Router 运行时 | **未做**（#880） |
| 通用查表 ResolveTableRow / TableRead* | **未做**（#881） |
| TagDisplay 专线 | **废止**（#876），不是待接线；清理 #877；文档纠偏 #879 |
| 本附录审计结论 | **PASS**（落地切片）；SSOT 文档残留见 handoff |

Epic 勾选建议与残留债指针：见 handoff 正文。
