# mass-nav 散分支处置审计

对照基线：`origin/main`（审计时与 `codex/gpu-skinned-commercial` HEAD `cffebc85a0` 同内容，0 领先）。
方法：每个远端分支跑 `git rev-list --count` 计领先/落后，`git cherry origin/main <branch>` 判内容是否已以 patch 等价形式进入 main，`git log origin/main..<branch>` 逐个读领先提交。
只审计，不动远端分支。

任务简报写"8 个"，实际以 `mass-nav` / `massnav` 命名的远端分支共 17 个，全部入表。其中 4 个是 main 的祖先（完全合并）、1 个领先提交全部 patch 等价于 main、12 个带未吸收内容。

## 处置表

| 分支 | 最后提交 | 领先 main | 领先提交内容 | 处置 |
| --- | --- | --- | --- | --- |
| `codex/1337-massnav-structural-add` | 2026-08-29 | 0（main 祖先） | — | 已在 main，纯历史，可删 |
| `codex/epic-416-mass-navigation-remediation` | 2026-06-27 | 0（main 祖先） | — | 已在 main，纯历史，可删 |
| `codex/mass-nav-pr129-cleanup` | 2026-05-10 | 0（main 祖先） | — | 已在 main，纯历史，可删 |
| `codex/masscrowd-core-runtime` | 2026-06-26 | 0（main 祖先） | — | 已在 main，纯历史，可删 |
| `codex/mass-nav-performer-minimap-integration` | 2026-05-08 | 3，全部 patch 等价已入 main | performer minimap parity（d070b8b500）、performer board SSOT（f818a64194）、minimap 顶层遮挡 HUD（67d7754dc4） | 内容已在 main，纯历史，可删；复核 #1520 的判断：确不含镜像修复 |
| `audit/mass-nav-20260824` | 2026-08-24 | 1 | 7b87afcf12 TEMP: e2e order-chain 插桩（DIAG2-6） | 临时诊断插桩，配套 `fix/massnav-e2e-order-chain` 的过程产物，纯历史，可删 |
| `codex/mass-nav-20k-rework` | 2026-04-10 | 1 | e9c087e2cb checkpoint mass flow nav playground | 单提交演练场快照，纯历史，可删（如需留档先打 tag） |
| `cursor/massnav-drop-visual-scale-77d9` | 2026-08-27 | 2，其中 1 为 CI retrigger 等价 | 3a820940d0 fix(massnav): drop visualScale from crowd execution | 待提取：1 个真实修复未入 main |
| `fix/massnav-e2e-order-chain` | 2026-08-24 | 2 | 6a0ecaccce fix(massnav,input): 右键指令链三重断裂 + CI 门禁（#1127）；75cb419a07 fix(massnav,assets): 三件 OBJ 三角化补 vt/vn 修 raylib LoadModel AV | 待提取：修复价值高，先确认 #1127 是否另有落地 |
| `codex/mass-nav-selection-followup` | 2026-07-05 | 5 | MassNavigation 指令改走 CommandSource/OrderQueue（7e7c0d12fb）、退役 selection 命名 API（5959635e29）、补 launch 流程（aac14d66a6）、收窄 copy helper（ffae405855） | 待提取：架构方向与主线的 command-source 收敛一致 |
| `codex/issue-642-massnav` | 2026-07-12 | 8 | MassNavigation cadence/capacity/streaming 可审计化（84918cd961 等 8 个，含调度合约漂移失败化、后台 steering 屏障、prepared storage） | 待提取：测试与合同加固，未见主线吸收 |
| `cursor/ord-1-massnav-input-removal-18f0` | 2026-07-04 | 19 | Epic #522 Phase D 收尾：MassNav 运行时 EntityView 解耦（574d7f9d5c）、Selection 双写退役（4a7ef87b49）、输入注册清理、docs/RFC-0061 更新 | 待提取：块大（19 提交），需与 main 的 input SSOT 线对齐后拣选 |
| `codex/mass-nav-arch-remediation` | 2026-05-08 | 28，其中 7 patch 等价已入 main | 等价的 7 个是 input/selection SSOT + heightmap/prefab + modding stream-load 块；净 21 个含 mass-nav 运行时边界形式化（ecf49147fb）、order/flow 接线（dbfeb6ec61）、关系感知避障（1999963025）、团队状态泛化（e39267a02d）、硬解 broadphase 优化（c594a55047）、visual terrain editor runtime（b5042951f3） | 待提取：先提取 mass-nav 侧，terrain editor 单独评估 |
| `codex/mass-nav-façade-audit` | 2026-04-13 | 17，其中 7 patch 等价已入 main | 等价块同上；净 10 个含 navigation2d 生产合同（974f0984d1）、有界 flow domain 池（50c72dbc94）及对错向 detour 的 revert（f17e77149f）、热同步裁剪（9ede37fcf7）、parking 修复（4952f80d3f） | 待提取：与 arch-remediation 同代，两者需先互相对账再提取 |
| `codex/mass-nav-bake-data-showcase` | 2026-06-18 | 3 | runtime navmesh bake showcase（eec395ec30）、调试 overlay 可读性（735f430710）、入门 slide deck（90291bb930） | 待提取：showcase + 文档资产 |
| `perf/massnav-10k-80fps` | 2026-09-10 | 30，全部含于 `cursor/massnav-10k-80fps-c959` | 10K 性能线：world-HUD 地形遮挡缓存容量（b37dd279c4）、minimap per-marker knowledge 消除（7fe343843b、9dd1b14497）、flow 避障 blocked-cell 索引（c407bd7d93）、culling 静态缓存修复（da4078076e、63583b5dd3）及大量 perf 分析文档 | 活跃：c959 的前身子集，以 c959 为提取口径，本支提取后转纯历史 |
| `cursor/massnav-10k-80fps-c959` | 2026-09-10 | 63（含 perf 支全部 30） | 在 perf 支之上：GpuSkinned 量化姿态共享（16db880988）、minimap 每像素 marker 帽 + 拒绝路径跳 knowledge（9541e4ba7c）、10K duplicate knowledge publisher 移除（9100297f51）、graph op 化 ShowMinimap/DiscloseCollection/SubmitOrder（949d845b00）、10K overlay 转 data-only mod（6a343a02c8）、mannequin 授权（f05f6e4ce0）等 | 活跃：最近提交 2026-09-10，是 10K 性能与 graph op 线的 SSOT 分支，不可删；与当前 gpu-skinned 商业化线（GpuSkinned 姿态共享同名工作）有交叠，合并前需对账 |

## 汇总

- 可删（纯历史）：6 支——4 个 main 祖先 + `performer-minimap-integration`（内容等价）+ `audit/mass-nav-20260824`（临时插桩）；`mass-nav-20k-rework` 视留档需求二选一。
- 待提取：10 支——修复类 2（`fix/massnav-e2e-order-chain`、`drop-visual-scale`），架构/合同类 5（`selection-followup`、`issue-642-massnav`、`ord-1-…-18f0`、`arch-remediation`、`façade-audit`），showcase 类 1（`bake-data-showcase`），活跃性能线 2（`perf/…` ⊂ `c959`，以 c959 为口径）。
- `arch-remediation` 与 `façade-audit` 共享 7 个已入 main 的等价提交，2026-04 同代产物，提取前互相 cherry 对账。
- `c959` ⊇ `perf/massnav-10k-80fps`（`git merge-base --is-ancestor` 验证），两支同线，删也只可能删 perf 支且须在 c959 提取之后。

审计分支：`codex/gpu-skinned-commercial`，2026-09-13。
