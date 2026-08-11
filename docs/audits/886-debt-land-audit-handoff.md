# 审计交接：#886 面板/图债收尾集中落地

**分支：** `cursor/ui-panel-debt-land-28e6`  
**Base：** `main`  
**全景入口：** #886  
**日期：** 2026-08-11

## 合并结论

| 子线 | Issue | 状态 |
|------|-------|------|
| 查表 SSOT 文档 | #876 / #879 | 已合入本分支 |
| UIP-0 ADR 锚点 | #880 | 文档已合入；运行时未做 |
| 架构审计（#875 切片） | #883 | handoff 已合入 |
| 通用查表实现 | #881 | 已合入 |
| TagDisplay 专线删除 | #877 | 已合入 |
| 编辑器样品 binding | #884 | 已合入 |
| showcase 配置卫生 | #882 | 已合入 |
| 热重载 | #874 | **不在本 PR**（DEFERRED） |

## 核心裁定

1. 查表唯一路径 = Mod/用户自建表 + `ResolveTableRow` / `TableRead*`  
2. TagDisplay 专线删除，禁止再接到 `GameEngine`  
3. 资源条 MVP 已在 main；本 PR 收债不重做玩法主路径  
4. UIP-0 仅合同文档，无 Template 运行时

## GAS Composition Gate

见 `artifacts/gas-composition-gate.md`（本落地综合自审：**PASS**）。

## 建议验证

```bash
dotnet test src/Tests/GasTests/GasTests.csproj \
  --filter "FullyQualifiedName~GraphLookupTableOpsTests|FullyQualifiedName~TagDisplayGraphOpsTests|FullyQualifiedName~UiPlayerAggregateGraphMvpShowcaseAcceptanceTests"
```

## 残留债（不阻塞本 PR）

- UIP-0 Template/Instance/Router **运行时**（#880 合同后的实现）
- #874 正式热应用（等 LSW）
- 人手关闭过时 issue：#870 / #871 / #878
- #880 issue 正文粘贴 ADR（token 无 issue 写权限时）
- showcase `UiPlayerAggregateGraphMvp`：`ModEntry.OnLoad` 仍用 resource stream 预注册 Attribute（`GasGraphSymbolResolver` 要求图 patch 前已 Register）；runtime 场景装载走 `ConfigPipeline`。`IModContext` 无 ConfigPipeline，统一权威需上移注册时机或扩展 Mod 上下文——本补线未改，避免碰 Attribute 冻结/装载序。
