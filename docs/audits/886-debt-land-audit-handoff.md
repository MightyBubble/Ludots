# 审计交接：#886 面板/图债收尾集中落地

**分支：** `cursor/ui-panel-debt-land-28e6`  
**Base：** `main`  
**全景入口：** #886  
**日期：** 2026-08-12

## 合并结论

| 子线 | Issue | 状态 |
|------|-------|------|
| 查表 SSOT 文档 | #876 | 已合入 |
| UIP-0 ADR 锚点 | #880 | 文档已合入；运行时未做 |
| #875 切片审计 | #883 | handoff 已合入 |
| 通用查表实现 | #881 | Core + ControlFlow 前门已合入 |
| TagDisplay 专线删除 | #877 | Core 已删；编辑器样品已改示范通用表 |
| 编辑器样品 binding | #884 | binding 互斥 + 校验已合入 |
| showcase 配置卫生 | #882 | 双真相收敛；离线阈值进配置；缺 buffer fail-closed |
| 热重载 | #874 | **不在本 PR**（DEFERRED） |

## 核心裁定

1. 查表唯一路径 = Mod/用户自建表 + `ResolveTableRow` / `TableRead*`（含 ControlFlow）  
2. TagDisplay 专线删除，禁止再接到 `GameEngine`；作者样板不得再示范废线  
3. 资源条 MVP 已在 main；本 PR 收债不重做玩法主路径  
4. UIP-0 仅合同文档，无 Template 运行时

## 审计阻塞项处置（本轮）

| 阻塞 | 处置 |
|------|------|
| 文档仍写「仍欠通用表装载」 | 已改为已交付装载路径 |
| 编辑器示范 `LookupTagDisplayText` | 已改为 `ResolveTableRow` + `TableReadInt` |
| `RefreshProducerMarkers` 静默 online + `0.01f` | 缺 buffer fail-closed；`offlineStockEpsilon` 进配置 |
| Attribute OnLoad vs ConfigPipeline 双路径 | Runtime 校验 pipeline 属性名必须等于 bootstrap，否则炸 |
| 交接过宣称 | 本文件已按实际完成度改写 |

## GAS Composition Gate

见 `artifacts/gas-composition-gate.md`（本落地综合自审：**PASS**）。

## 建议验证

```bash
dotnet test src/Tests/GasTests/GasTests.csproj -c Release \
  --filter "FullyQualifiedName~GraphLookupTableOpsTests|FullyQualifiedName~TagDisplayGraphOpsTests|FullyQualifiedName~UiPlayerAggregateGraphMvpShowcaseAcceptanceTests|FullyQualifiedName~PanelProjectionReaderTests|FullyQualifiedName~GraphQueryControlFlowTests"
```

## 残留债（明确不在本 PR）

- UIP-0 Template/Instance/Router **运行时**（#880 合同后的实现）
- #874 正式热应用（等实时技能工作台正式管线，勿用 ReloadConfigs 捷径冒充）
- 玩法纯读 Effective tag → tag id 原子能力（作者样板节点 `EffectiveStateTagId` 仅为意图占位）
- Text BB；表面 token→文案接线
- 人手关闭过时 issue：#870 / #871 / #878
- #880 issue 正文粘贴 ADR（token 无 issue 写权限时）
