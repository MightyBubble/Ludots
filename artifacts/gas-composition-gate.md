## GAS Composition Gate — Self Review

- **Task / Issue**: #861 Epic — GAS L1 图作者 SSOT 收敛（单一边模型 + 唯一编译前门）
- **Date**: 2026-08-11
- **Agent / Author**: cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（统一作者边模型与唯一编译前门；不新增 profile enum / preset 开关）

结论: **PASS**

一句话理由: 把 Effect/Score/Validation/Derived 收进已有 ControlFlow 边模型，用 Kind 过滤 op 能力矩阵，废除按 JSON 外形猜编译器。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 作者 schema（controlEdges/valueEdges） | 2 | `GraphControlFlowDocument` |
| Kind → 唯一编译前门 | 2 | `GraphProgramAuthoringFrontDoor` + Loader/Bridge |
| op×Kind 作者白名单 | 2 | `GraphControlFlowCompiler` + `GraphAuthoringKindPolicy` |
| 可执行 IR | 0 | 既有 `GraphInstruction[]` / L0 VM |
| 运行时 opcode 策略 | 0/2 | 既有 `GraphKindOperationPolicy`（不平行） |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`（不改 opcode 语义）
- Queues / Systems: 无新 System
- Resolvers / Registries: `GraphProgramRegistry`、`GraphIdRegistry`、`GraphProgramSymbolPatcher`
- Existing presets / graphs: 仓库内 Effect/Score/Derived `GAS/graphs.json` 迁到 CF 边模型

### 4. New Layer 0 ops (if any)

N/A — 不新增 `GraphNodeOp`。

### 5. Transaction boundary

必须原子 rollback 的步骤: 无（本任务只改作者编译前门，不改 lifecycle 事务）。

### 6. Config SSOT

行为配置落在: `GAS/graphs.json`（ControlFlow 作者文档）+ 现有 effect template

是否新增 JSON schema: **NO** — 扩展既有 CF 文档字段（`effectTemplate` / `builtinHandler`），废除 next-chain 作为运行时真相。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（Loader 不再外形猜编译器；next-chain 硬拒）

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**

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
