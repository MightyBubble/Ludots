# GAS Composition Gate — TextKey discovery sugar (`LoadTextKey`)

- **Task / Issue**: TextKey 发现糖（GameplayTag 式选键）→ 运行时仍走 `PresentationTextCatalog` i18n；与 FormalText 字面量轨分离
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Cloud Agent on `cursor/textkey-discovery-sugar-e967`

## GAS Composition Gate — Self Review

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新增 graph 节点 `LoadTextKey` + 编辑器发现 API；不新增 profile enum / preset 开关）

结论: **PASS**

一句话理由: 作者侧只加「按键名发现」的图 op 与 Bridge 名册；运行时复用既有 TextToken catalog / formatter 合同，不平行造第二套本地化表，也不把 key 塞进 `ConstText`。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| `LoadTextKey`（查表 → Text 寄存器） | 0 | `GasGraphOpHandlerTable` + `GraphTextHeap` |
| 符号 patch：`textKey` → tokenId | 0 | `GraphProgramSymbolPatcher` + `IGraphSymbolResolver.ResolveTextToken` |
| 编辑器发现名册 | 3（工具面） | Bridge `GET /api/graph/text-keys/{modId}` + React `textKey` 字段 |
| 词条数据 | 配置 SSOT | 既有 `Presentation/text_tokens.json` + `text_locales.json` |

### 3. Reuse list

- Handlers: FormalText 的 Text 堆 / `SinkPresentationText` 出口；`PresentationTextCatalog` / `PresentationTextFormatter`（本切片零参拷贝模板 Source，不热路径 StringBuilder）
- Queues / Systems: 无新队列
- Resolvers / Registries: `GasGraphSymbolResolver`、`PresentationTextCatalogLoader`、`ConfigKeyRegistry`/`Symbols` intern 模式（仿 `HasTag`）
- Existing presets / graphs: Gallery FormalText 短剧模式；Story Line 的 `textToken` 字段（编辑器选择器同源名册）

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `LoadTextKey=461` | 按 TextToken 键取默认 locale 模板写入 Text 槽 | `ConstText` 是字面量进 Symbols 且禁止 patch；查表 + i18n 是另一轨，禁止把 key 伪装成字面量 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（只读 catalog → 写 Text 槽；失败关闭，不半句截断）

### 6. Config SSOT

行为配置落在: graph 节点字段 `textKey` + catalog（`Presentation/text_tokens.json` / `text_locales.json`）

是否新增 JSON schema: **NO** — 复用既有 token/locale 表；Bridge 只投影名册

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未知键 / 缺模板 / 声明有参却零参加载 → 失败关闭）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线 / 字段**（例如后续 `FormatTextKey` 接 Int/Float 参） / effect 步骤 — **不**改 Core enum 行为开关

若选了 Core enum → FAIL — 本任务未选
