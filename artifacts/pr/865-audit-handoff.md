# 审计交接：#865 Roslyn/ALC codegen（可合并版）

**PR：** https://github.com/MightyBubble/Ludots/pull/865  
**分支：** `cursor/graph-codegen-control-flow-b361`  
**Base：** `main`  
**范围：** #860 R0 + Track C（Jump/JumpIfFalse）GasTests spike；**不含** Script 作者糖（糖在 [#869](https://github.com/MightyBubble/Ludots/pull/869)）。

---

## 合并结论

| 项 | 状态 |
|----|------|
| 本 PR（codegen） | **可独立合入 main**（与 #859/#869 无依赖） |
| 作者糖 | **不在本 PR** → [#869](https://github.com/MightyBubble/Ludots/pull/869) |
| #862 | R0 子集；合本 PR 后可关闭 |

```text
main ← #865（本 PR，codegen）
main ← #859 ← #869（糖，另线）
```

---

## 审计门禁对照

| 原阻断 | 本版处置 |
|--------|----------|
| `ci-gate` 夹带微基准硬阈值 | 类级 `ci-gate` 已移除；正确性测方法级 `ci-gate`；`Microbench_*` 仅 `benchmark`，去掉 ratio assert |
| Score + next-chain `GraphConfig` 样例 | 改为 `GraphInstruction[]` IR 合同测；spike 边界不吃作者层 |
| 平行 ALC 宿主策略 | `GraphGeneratedAssemblyLoadContext` 标明 Tests spike；R2 抽共享 helper，不以本类型为生产 SSOT |
| 后向 Jump 无限循环 | emitter 拒绝 `Imm < 0`（失败关闭） |

---

## 验证

```bash
dotnet test src/Tests/GasTests/GasTests.csproj \
  --filter "FullyQualifiedName~GraphRoslynAlc" \
  -o /tmp/pr865-codegen
```
