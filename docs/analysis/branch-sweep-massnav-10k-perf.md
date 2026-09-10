# 本地/远端分支清点：还有谁在做 10K 性能这条线

扫描方式：对全部 `refs/heads` + `refs/remotes/origin` 中
**2026-09-01 之后有提交**、**tip 不在本分支（`06535b8750`）里**、且
**改动 `src/` 文件数 < 60** 的分支逐条核对（避免把巨型重构线混进来）。

## 结论：只有 1 条分支与本任务直接重叠，且它已经吸收了本分支的全部成果

### 🔴 `codex/presenter-attachment-contract-perf`（**活跃并行会话，直接重叠**）

| 项 | 值 |
|---|---|
| 本地 tip | `4a9a5c58b6`（**2026-09-10 09:57**，今天） |
| 远端 tip | `4416820854`（PR #1486 原始 4 提交） |
| 来源 | `C:\001_AI\Ludots-presenter-attachment-contract-perf`（活工作树） |
| 相对本分支 | +16 commits / 43 src files |

**它在做的事**：把 PR #1486 的主体**rebase 到本分支的全部性能提交之上**。
它的 16 个提交里，有 **11 个是本分支提交的 rebase 副本**（SHA 不同、内容相同）：

```
4a9a5c58b6 chore(perf): drop a stray screenshot...        ← 本分支 a6e74e196d
8ea20fc73b test(presentation): lock the terrain occlusion cache capacity contract  ← 06535b8750 里的
839b0b04a3 perf(hud): size the world-HUD terrain occlusion cache...                ← b37dd279c4
8b1341bb21 perf(knowledge): hoist the resolver and tick...                          ← 9dd1b14497
f729eaf849 perf(minimap): resolve marker knowledge...                               ← 7fe343843b
35bdec4390 fix(culling): keep the present-binding pass current...                    ← 63583b5dd3
2c198e4f1d perf(massnav): gather flow obstacle avoidance...                          ← c407bd7d93
fa8e6c58ef perf(render): cull shadow casters...                                      ← 7c366ab0f2
c05f69f61f fix(culling): stop the per-frame present-binding rebind...                ← da4078076e
42681b3178 perf(culling): replace spatial-candidate HashSet...                       ← 6b136c79db
8412418381 perf(massnav): fuse penetration probe...                                   ← f9b73dea3d
```
其余 5 个是 #1486 自身的 attachment/parameter 契约提交（`a6432ea57d`、`d9cce5b5da`、
`b13c3162a7`、`b0868459d3`、`6925aee12d`）。

**它已经修掉了我先前报告的 #1486 阻断项**：
`MassNavigationMod/presenters.json` 里对不存在资产 `mass_navigation.command.marker` 的引用，
在该分支上已降为 **0 处**（原始远端分支仍有 2 处）。`6925aee12d fix(navigation): reuse shared
selection presenters` 就是这一修。**所以 #1486 现在具备合入前提。**

**建议**：不要与本分支重复合并 —— 本分支是它的上游。正确顺序是
**先合本分支（或让它 rebase 到最新 main），再由它收口 #1486**。
它的活工作树正在跑 10K 基准（`.tmp-run-10k.cmd`：auto-orbit 20°/s、AgentBridge 48060、
`DOTNET_TieredCompilation=0`），说明在做整合后的复测。

### 🟡 `codex/gpuskinned-regression`（**过期的 #1485 前身，无需动作**）

44 files / +2509−291 vs main，但**逐内容核对确认其成果已在 main**：

| 其提交 | 内容 | main 是否已有 |
|---|---|---|
| `e9bbfdb4cd` | 姿势调色板分页 | ✅ `RaylibPoseTexturePalette.cs` 里 `BoneSlotsPerRow`/`SlotRowsFor` 20 处命中 |
| `a20892f0eb` | 10K presenter/collision 热路径移除 | ✅ `_hardResolveCandidates` 9 处命中 |
| `f7a197e862` | health drift 容量契约 | ✅ `game.json` 有 `deferredTriggerPerFrameCapacity` |

它是 PR #1485 合并前的原始线；#1485 以等值内容进了 main。**不要合，会污染历史。**

### 🟢 `codex/effect-transaction-performance` / `pr1463`（**重复，应关闭**）

已在本会话早先核对：实现 + 规模测试 + 文档**逐字节已在 main**（`TransactionEntityIndex.cs` 等无差异）。
`origin/codex/effect-transaction-performance` 不在 main 只是因为它是**旧基线**，不是内容缺失。

### ⚪ 其余（与 10K 帧率目标无关，仅登记）

| 分支 | 性质 |
|---|---|
| `codex/entity-attachment-motion-contract`（#1488） | GAS `AttachmentPositionSyncSystem`，非 presenter 热路径 |
| `integrate/activity-1296-line`（#1487） | activity 线验收引用修复 |
| `codex/navmesh-board-addressing-m1`（#1484） | navmesh 每板寻址，与每帧无关 |
| `codex/massnav-case-e-selection` | Case E 选择链路 |
| `codex/presenter-retained-visibility`（#1470） | presenter 可见性/清理 |
| `codex/issue-1480-contact-emission` | 物理接触触发 |
| `codex/1398-debt-d7-d8`、`codex/issue-1402-height-ssot`、`codex/epic-1436-all`、`codex/in-app-launcher-shell`、`cursor/1398-case-e-press-px-*` | 各自独立线 |

## 重复实现风险提示

`codex/presenter-attachment-contract-perf` 的存在意味着**同一批优化正在两个分支上各有一份**。
为避免双份推进，本分支不再吸收 #1486 的 attachment 契约内容；
#1486 由那条线收口，本分支只作为它的性能上游。
