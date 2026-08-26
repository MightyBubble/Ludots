## GAS Composition Gate — Self Review

- **Task / Issue**: 图正式文字基建（text value / 字符串寄存器 / 花括号自动引脚 / Concat / presentation sink）
- **Date**: 2026-08-26
- **Agent / Author**: Cursor Grok Cloud Agent
- **Branch**: `cursor/graph-formal-text-e967`

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 交付物是 graph 节点与作者糖组合——固定容量 Text 寄存器 + ConstText/ConcatText/IntToText/FloatToText/SinkPresentationText 原子 op，FormatText 降为片段拼接；不新增 profile enum / 平行文案管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Text 值合同与固定容量堆 | 0 | `GraphValueType.Text` + `GraphTextHeap` + `GraphVmLimits` |
| ConstText / ConcatText / IntToText / FloatToText | 0 | `GraphNodeOp` + handler + descriptor |
| SinkPresentationText | 0 | op + `IGraphRuntimeApi` + `GraphPresentationTextSink` |
| FormatText 花括号自动引脚 | 2 | `GraphAuthoringSugar.FormatText` → ConstText + Concat(+ Int/FloatToText) |
| 符号表字面量 | 0 | 程序 `Symbols[Imm]`；ConstText Imm 不 patch |
| 编辑器露出 | 2 | Bridge descriptor / sugar 仅在运行时名册齐后投影 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` RegisterBuiltins 模式（对齐 ConstInt/AddInt/ShowPanel）
- Queues / Systems: 既有 GraphExecutor / GraphFrame；不新造 VM
- Resolvers / Registries: 程序符号表作字面量池；`GraphPresentationTextSink` 固定环作表现出口
- Existing presets / graphs: PresentationTextCatalog `{0}` 占位规则；Story/Dialogue 仍以 TextToken 为配置态文案 SSOT，图内拼句走 sink 运行态出口

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ConstText | 字面量写入 Text 槽 | 现无 Text 银行与文字 op |
| ConcatText | 两段 Text 拼接 | 表查表路径明确禁止字符串聚合；需独立 Text 路径 |
| IntToText / FloatToText | 标量→Text | 无现成转换 op |
| SinkPresentationText | Text→字幕/对话出口 | 无现成 presentation sink |

### 5. Transaction boundary

无实体生命周期事务。容量越界 / 缺 sink / 未终止花括号 → 失败关闭，不回滚半写实体。

### 6. Config SSOT

行为配置落在: graph JSON（`text` 字段 + FormatText 糖）+ 运行时 descriptor 名册

是否新增 JSON schema: **NO**（节点字段增量；不新开 catalog 表）

合同正文: `gitbook/architecture/graph-formal-text.md`；进度钉在 `graph-capability-status.md` / `graph-editor-and-live-debug.md`。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback；容量满 / 缺符号 / 缺 sink / 假节点一律失败关闭

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**
