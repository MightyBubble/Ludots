## GAS Composition Gate — Self Review

- **Task / Issue**: #877 chore(graph): 移除 TagDisplay 查表专线实现
- **Date**: 2026-08-11
- **Agent / Author**: Cloud Agent (cursor/remove-tagdisplay-specialty-28e6)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **D — 删除误开平行专线，不新增变体**

结论: **PASS**

一句话理由: 本单只删除 `TagDisplay*` / `LookupTagDisplay*` / 绑 Display 表的 `SelectTagInMask`，不新增 profile enum、不实现 `ResolveTableRow`（归 #881）。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 删除 LookupTagDisplayToken / SelectTagInMask | L0 回撤 | GraphNodeOp + Handler + Compiler |
| 删除 TagDisplayTableRegistry | 错误基建回撤 | Presentation.TagDisplay |
| 状态文案正线 | L2 作者资产（另单） | 通用表 + ResolveTableRow (#881) |

### 3. Reuse list

- Handlers: 既有 GasGraphOpHandlerTable（删分支，不平行表）
- Queues / Systems: 无变更
- Resolvers / Registries: 去掉 TagDisplay 注入；保留 TagRegistry / HasTag
- Existing presets / graphs: 无生产图依赖 TagDisplay（未接入 GameEngine）

### 4. New Layer 0 ops (if any)

N/A — 本单删除 op，不新增。

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A（纯删除）

### 6. Config SSOT

行为配置落在: 文档正本 `gitbook/architecture/graph-table-lookup.md`；实现归 #881

是否新增 JSON schema: **NO**

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] **未**把 TagDisplay 接到 GameEngine 当完成

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（作者自建 lookup 表 + 通用查表 ops，#881）

若选了 Core enum → FAIL — 未选。

### SelectTagInMask 结论

**删除**。现行实现 `Imm=tableId` 取自 `TagDisplayTableRegistry.GetMask`，无法与 Display 专表解耦；作者糖 `ReadGameplayTag` 一并移除。纯读 Effective tag→tagId 若另需，开独立子单，禁止绑 Display 表。
