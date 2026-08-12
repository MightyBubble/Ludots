# 审计交接：#858 / #875 落地架构审计

| 字段 | 值 |
|------|-----|
| Issue | [#883](https://github.com/MightyBubble/Ludots/issues/883) |
| Epic | [#858](https://github.com/MightyBubble/Ludots/issues/858) |
| 落地 PR | [#875](https://github.com/MightyBubble/Ludots/pull/875)（已合 main） |
| 全景入口钉 | [#886](https://github.com/MightyBubble/Ludots/issues/886) |
| 查表 ADR | [#876](https://github.com/MightyBubble/Ludots/issues/876) |
| 审计基线 | `origin/main` @ `f394f8742`（Merge #875） |
| 日期 | 2026-08-11 |
| 本文件路径 | `docs/audits/875-or-858-audit-handoff.md`（可提交） |
| 约定路径 | `artifacts/pr/875-or-858-audit-handoff.md` 被 `.gitignore` 的 `artifacts/` 挡住，故正本放本目录；本地可复制到 artifacts |

---

## 审计结论：**PASS**

**#875 已进 main 的资源条 MVP 切片**在分层、fail-closed、验收禁手写求和、禁止平行 Presentation Graph 上符合 #858 合同。  
**未做**项与 **TagDisplay 专线废止**已写明，不得再被当成「待接线」。

残留债见文末指针；其中 **main 上 gitbook 仍残留 TagDisplay 正线叙述** 已由开放 PR [#879](https://github.com/MightyBubble/Ludots/pull/879) / ADR [#876](https://github.com/MightyBubble/Ludots/issues/876) 跟踪，**不**在本审计单内重写查表正本。

`artifacts/gas-composition-gate.md` 主体仍是 **#861**；本审计不覆盖该文件，仅在其末追加「#858/#875 附录」指针（本地 artifacts，gitignore）。

---

## 一句话真相地图

```text
已做（main / #875）
  Query 图 → GraphReturnWriter → GraphOutputValueStore
    → PanelProjectionReader → Surface（资源条 MVP + 编辑器作者样板）

未做
  UIP-0 Template / Instance / Router 运行时（#880）
  通用查表 ResolveTableRow / TableRead*（#881）

废止（不是待接线）
  TagDisplay 专线（ADR #876；清理 #877；文档纠偏 #879）
```

---

## 清单对照（#883 最低项）

### 1. 分层 — **PASS**

| 层 | main 证据 | 结论 |
|----|-----------|------|
| Query 投影 | showcase `assets/GAS/graphs.json`：`ui.panel.player.resource.aggregate`（`AggSumAttribute` → outputs） | 复用 #848 L1，无第二 VM |
| GraphOutputValueStore | `UiPlayerAggregateGraphMvpRuntime.ExecuteAggregateGraph` 经 `GraphReturnWriter` 物化后读 Store | 正式 summary 物化 |
| PanelProjectionReader | `src/Core/UI/PanelProjection/PanelProjectionReader.cs`；showcase runtime 用 `AggregateProjection` 绑定 | 统一读口，无平行 panel value store |
| Surface | `UiPlayerAggregateGraphMvpPanelController` / PresentationSystem 只画投影结果 | Presentation 不重算合计 |

`PanelBindingSourceKind` 仅：`SingleAttribute` / `DerivedAttribute` / `AggregateProjection` / `GraphOutput`。  
仓库内无 `GraphKind.Presentation`、无 `GraphNodeOp.Panel`。

### 2. Fail-closed — **PASS**

`PanelProjectionReader`：

- 缺 live owner → 抛
- 缺 `GraphOutputValueStore` / 缺 summary key → 抛（文案含 `Silent zero is forbidden`）
- 缺 attribute / 未定义槽 → 抛

单元测试：`src/Tests/GasTests/UI/PanelProjectionReaderTests.cs`。

生产路径：`GasGraphRuntimeApi.CreateProduction(...)` **不**注入 `TagDisplayTableRegistry`；`GameEngine` 亦未 `SetService(TagDisplayTableRegistry)`。专线即便残留代码，也未进生产接线。

### 3. 验收禁手写求和 — **PASS**

`UiPlayerAggregateGraphMvpShowcaseAcceptanceTests`：

- 面板数字 == `GraphOutputValueStore` summary
- `AssertNoPresentationEntitySum`：Presentation/UI 文件禁止 `QueryAllMapEntities` / `SumAttribute` / `AttributeBuffer` / `world.Query` 等
- 测试 oracle 只用 seed 配置求和，明确不是 presentation 路径

验收产物目录：`artifacts/acceptance/ui-player-aggregate-graph-mvp/`（gitignore，CI/本地生成）。

### 4. SSOT 互指 / TagDisplay 正线残留 — **已由 #893 收口（见 #886 handoff）**

| 源 | #893 后状态 | 裁定 |
|----|------------|------|
| Issue ADR [#876](https://github.com/MightyBubble/Ludots/issues/876) | 通用表唯一路径；TagDisplay **废止** | 计划 SSOT |
| 全景 [#886](https://github.com/MightyBubble/Ludots/issues/886) | 写明废止与禁止接入生产 | 债地图 |
| `gitbook/.../graph-table-lookup.md` | 唯一查表正本 | **已纠偏** |
| `gitbook/.../tag-display-lookup.md` | SUPERSEDED 短页 | **已纠偏** |
| `gitbook/.../ui-panel-authoring-form.md` | curState 走通用表；装载已交付 | **已纠偏** |
| 编辑器 `/ui-panel-authoring` | 示范 `ResolveTableRow` / `TableReadInt` | **已纠偏** |

审计合同口径：

> **TagDisplay 专线废止，不是待接线。**  
> 不得再把 `TagDisplayTableRegistry` / `LookupTagDisplayToken` 接入 `GameEngine` / `CreateProduction`。  
> 实现查表走 [#881](https://github.com/MightyBubble/Ludots/issues/881)；删除专线代码走 [#877](https://github.com/MightyBubble/Ludots/issues/877)。

### 5. UIP-0 状态 — **未做（明确）**

| 项 | 状态 |
|----|------|
| Template / Instance / Router **运行时** | **未落地** |
| 静态讨论原型 | `docs/prototypes/ui-panel-template-instance-prototype.html`（讨论用） |
| 作者形态文档 | `gitbook/architecture/ui-panel-authoring-form.md`（样板 ≠ 运行时） |
| 合同 ADR 子单 | [#880](https://github.com/MightyBubble/Ludots/issues/880) |

资源条 showcase 是「手挂表面 + Projection 读口」过渡形态；**不得**假装已有 Template 装载运行时。

### 6. 门禁产物 — **本文件**

- 可提交正本：`docs/audits/875-or-858-audit-handoff.md`
- 不覆盖 `artifacts/gas-composition-gate.md`（#861 主体保留）；附录见该文件末尾「Appendix: #858/#875」

---

## 已做 / 未做 / 废止（交付表）

### 已做（勿重复造）

| 能力 | 指针 |
|------|------|
| 玩家资源总览 MVP | showcase `ui_player_aggregate_graph_mvp`；Query→GraphOutput→`PanelProjectionReader` |
| 统一投影读口（MVP 子集） | `src/Core/UI/PanelProjection/*` + GasTests |
| 编辑器作者样板 | `Ludots.Editor.React` 路由 `/ui-panel-authoring`；导出 `ludots.ui.panel_template/v1` |
| 图分层前门相关 | 随 #848 / #861 / #869 等已在 main（本审计不重审 L1 前门细节） |

### 未做（仍开）

| 主题 | Issue |
|------|-------|
| UIP-0 Template/Instance/Router 合同 ADR | [#880](https://github.com/MightyBubble/Ludots/issues/880) |
| 通用查表实现 | [#881](https://github.com/MightyBubble/Ludots/issues/881) |
| TagDisplay 专线代码清理 | [#877](https://github.com/MightyBubble/Ludots/issues/877) |
| showcase 配置卫生 | [#882](https://github.com/MightyBubble/Ludots/issues/882) |
| 编辑器样品与 `PanelVariableBinding` 对齐 | [#884](https://github.com/MightyBubble/Ludots/issues/884) |
| 过时 PR/占位卫生 | [#885](https://github.com/MightyBubble/Ludots/issues/885) |
| UIP-3/4/5… | 仍归 #858，未开打 |
| 热重载正式化 | PR [#874](https://github.com/MightyBubble/Ludots/pull/874) **DEFERRED** |

### 废止（禁止「待接线」叙事）

| 项 | 状态 |
|----|------|
| TagDisplay 专表 / 专 op 作为产品正线 | **废止**（#876） |
| 把 #868 TagDisplay 部分继续扩展合入 | **禁止**（有价值部分 `PanelProjectionReader` 已随 #875 在 main） |
| Core 预置业务映射表 | **禁止** |

代码残留（registry / opcode / 测试）= **清理债 #877**，≠ 批准生产接线。

---

## 建议如何更新 #858 勾选

无法直接 edit Epic 时，以 [#886](https://github.com/MightyBubble/Ludots/issues/886)「Epic 进度补丁」为准；维护者可将 #858 正文勾选改为：

| 子单 | 建议勾选 | 备注 |
|------|----------|------|
| **UIP-0** ADR/合同 | ☐ 保持未勾 | 合同单 [#880](https://github.com/MightyBubble/Ludots/issues/880)；运行时未做 |
| **UIP-1** 资源条 MVP | ☑ **勾选（已进 main / #875）** | showcase 卫生另见 #882，不挡 UIP-1 完成认定 |
| **UIP-2** 统一 Projection 读口 | ◐ **部分完成**（建议正文改为半勾说明，或拆：「读口 MVP ☑ / Template 装载 ☐」） | `PanelProjectionReader` 已在；完整 Template 装载未做 |
| **UIP-3**…**UIP-6** | ☐ | 未开打 |
| **UIP-7** Guardrails | ◐ 部分 | MVP 验收已禁 presentation 手写求和 + reader fail-closed；Epic 级全面 guard 仍开 |
| 查表 | 不在原 UIP 列表；旁注 | ADR #876 · 实现 #881 · 清理 #877 · 文档 #879 |
| 架构审计 | ☑ 本单 #883 | 以本文为据 |

建议在 #858 顶部加一行指向：全景 [#886](https://github.com/MightyBubble/Ludots/issues/886) + 本审计 `docs/audits/875-or-858-audit-handoff.md`。

---

## 残留债指针（给后续 Agent）

1. **[#879](https://github.com/MightyBubble/Ludots/pull/879)** — 合入后消灭 main gitbook TagDisplay 正线残留（本审计 SSOT 项依赖它转绿）。  
2. **[#880](https://github.com/MightyBubble/Ludots/issues/880)** — UIP-0 ADR（勿在 showcase 私造第二套挂载真相）。  
3. **[#881](https://github.com/MightyBubble/Ludots/issues/881)** — 通用查表实现（唯一查表路径）。  
4. **[#877](https://github.com/MightyBubble/Ludots/issues/877)** — 删除 TagDisplay 专线实现。  
5. **[#882](https://github.com/MightyBubble/Ludots/issues/882)** — aggregate showcase 配置卫生。  
6. **[#884](https://github.com/MightyBubble/Ludots/issues/884)** — 编辑器样品去掉 LookupTagDisplay 正线文案，对齐 `PanelVariableBinding`。  
7. **[#885](https://github.com/MightyBubble/Ludots/issues/885)** — 关闭/标明 superseded 的开放 PR（#855/#863/#864/#868/#872/#873 等）。  
8. **[#874](https://github.com/MightyBubble/Ludots/pull/874)** — 热重载保持 DEFERRED。

---

## 本单边界（复述）

- 交付：审计结论 + Epic 勾选建议 + 残留债指针。  
- **不**实现 UIP-0 / ResolveTableRow / TagDisplay 清理代码。  
- **不**整份覆盖 #861 的 `gas-composition-gate.md`。
