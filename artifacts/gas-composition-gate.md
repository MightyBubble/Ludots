## GAS Composition Gate — Self Review

- **Task / Issue**: #886 全景收尾落地（#881 通用查表 + #877 删除 TagDisplay 专线 + 文档/showcase/审计）
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent (cursor/ui-panel-debt-land-28e6)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A + D**（新增通用查表 L0 ops；删除误开 TagDisplay 专线）

结论: **PASS**

一句话理由: 查表收成唯一用户/Mod 表路径；TagDisplay 专线删除而非接入生产；不新增 profile enum / 平行 Presentation Graph。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| ResolveTableRow / TableReadInt / TableReadFloat | 0 | GraphNodeOp + Pure handler |
| GraphLookupTableRegistry / Loader | 0 只读索引 + 配置装载 | StringIntRegistry + ConfigPipeline |
| 删除 LookupTagDisplay* / SelectTagInMask / TagDisplayTableRegistry | L0/基建回撤 | GraphOps + Handler + Presentation.TagDisplay |
| 作者表语义（rank/state_display…） | 3（内容） | Mod JSON |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` Pure 分类
- Resolvers: `IGraphSymbolResolver` / `GraphProgramSymbolPatcher` / `GasGraphRuntimeApi`
- Config: `ConfigPipeline`、`PresentationTextCatalog`（TextToken id）
- 无新 System / 无平行 VM

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ResolveTableRow | key → rowHandle | 现有 op 无通用表索引 |
| TableReadInt | rowHandle+field → Int/TextToken id | typed 只读字段 |
| TableReadFloat | rowHandle+field → Float | 同上 |

删除：`LookupTagDisplayToken`、`SelectTagInMask`（及作者糖）。

### 5. Transaction boundary

必须原子 rollback 的步骤: **无**（纯读查表；删除专线无 ECS 写事务）

### 6. Config SSOT

行为配置落在: graph 连线 + Mod `GraphTables/lookup_tables.json`

是否新增 JSON schema: **YES** — 通用 lookup 表内容合同（非 profile DSL）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（缺表/缺行 fail-closed）
- [x] **未**把 TagDisplay 接到 GameEngine 当完成

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换表 id / 字段 / key 来源）

若选了 Core enum → FAIL

### SelectTagInMask 结论

**删除**。现行实现绑 Display 表 mask，无法解耦；纯读 Effective tag→tagId 若另需，开独立子单，禁止绑 Display 表。

---

## Appendix: #858 / #875 面板投影落地审计（#883）

> 正本：`docs/audits/875-or-858-audit-handoff.md`

| 项 | 结论 |
|----|------|
| 资源条 MVP + PanelProjectionReader + 编辑器样板 | **已做**（#875 / main） |
| UIP-0 Template/Instance/Router 运行时 | **未做**（合同 ADR 文档见本落地 PR / #880） |
| 通用查表 ResolveTableRow / TableRead* | **本落地 PR 实现**（#881） |
| TagDisplay 专线 | **本落地 PR 删除**（#877）；废止 #876 |
| showcase 配置卫生 | **本落地 PR**（#882） |
| 本附录审计结论 | **PASS**（落地切片）；UIP-0 运行时仍为后续债 |

Epic 勾选建议与残留债指针：见 handoff 正文与全景 #886。
