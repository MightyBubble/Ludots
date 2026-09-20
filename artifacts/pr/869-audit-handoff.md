# 审计交接：#869 Script 控制流作者糖（可合并版）

**PR：** https://github.com/MightyBubble/Ludots/pull/869  
**分支：** `cursor/graph-cf-sugar-integrate-b361`  
**Base：** `cursor/ui-panel-graph-mvp-28e6`（#859）  
**范围修订（相对首版交接）：** **已剥离 #860 Roslyn/ALC codegen**（回应审计：勿把 codegen 绑 #859 UI 栈）。codegen 走 [#865](https://github.com/MightyBubble/Ludots/pull/865)（main）。

---

## 合并结论

| 项 | 状态 |
|----|------|
| 本 PR（糖） | **#859 放行后可合**；相对 #859 仅作者糖 + 文档 + SSOT |
| Codegen | **不在本 PR** → [#865](https://github.com/MightyBubble/Ludots/pull/865) / [#862](https://github.com/MightyBubble/Ludots/pull/862) |
| #866 / #867 | **勿合**（已被本 PR 取代） |
| #863 | 合入时以 `GraphAuthoringSugar` + gitbook 糖表为名册；ParseOps 需 rebase，糖保持 Script-only |

```text
main ← #859 ← #869（本 PR，仅糖）
main ← #865（codegen，独立）
```

---

## 本 PR 交付

- `BranchBool` / `SwitchInt` / `Wait`→Yield / `While` / `Until`（编译期降级，无新 L0 op）
- `AuthoredOpKind`：1/2/3/4
- SSOT：`GraphAuthoringSugar` + `gitbook/architecture/graph-layering-flow-and-behavior.md`「Script 作者糖」
- Effect / Query 禁糖与禁 Yield 测试

## 验证

```bash
dotnet test src/Tests/GasTests/GasTests.csproj \
  --filter "FullyQualifiedName~GraphBranchSwitchSugarTests|FullyQualifiedName~GraphScriptWaitLoopSugarTests" \
  -o /tmp/pr869-sugar
```

## 审计门禁对照（Cursor Automation）

| 原阻断 | 本版处置 |
|--------|----------|
| ci-gate 夹带微基准 | codegen 已移出本 PR；在 #865 修复 |
| codegen 绑 #859 | **已剥离** |
| 糖 SSOT / gitbook / #863 | **已补** `GraphAuthoringSugar` + gitbook；#863 合并约定已写明 |
| FromGraphConfig next-chain | 随 codegen 移至 #865 修复 |
| 交接过度乐观 | 本文件改为：**糖在 #859 后可合；codegen 另线** |
