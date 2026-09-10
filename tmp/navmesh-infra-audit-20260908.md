# NavMesh 基建 TODO 审计与里程碑拆分
审计基线：`codex/navmesh-ssot-feature-catalog` @ `10892035e2`（干净 worktree `C:/001_AI/_navmesh_audit_20260908`）
导航代码基线：与 `main` @ `81a1b5f543` **逐字节相同**（`src/Core/Navigation` tree hash 双方均为 `747fdccff024fcb4d258fcd40216248218b2d2b8`）
审计日期：2026-09-08

---

## 0. 首先纠正一个关键事实：文档还没进主线

| 事实 | 证据 |
|---|---|
| `gitbook/navmesh-ssot.md` 与 `gitbook/navmesh-features/`（25 个文件：24 专题 + README）**只存在于 `codex/navmesh-ssot-feature-catalog`** | `git ls-tree -r --name-only main -- gitbook/navmesh-features/` 返回空 |
| 该分支**未合并**，领先 main 80 个提交 | `git branch --merged main` 无此分支；`git rev-list --count main..<branch>` = 80 |
| 该分支**导航代码零改动** | `git diff --name-only main <branch> -- src/Core/Navigation/` = 0 行 |
| 分支还夹带了 176 个非导航 src 文件改动 | 来自 80 个提交中不相关的 GasTests/Editor/Presenter 工作 |

**结论**：这个分支是**纯文档交付**。你看到的线上 24 个专题页面是文档，不是代码状态。所以"根据文档调查基建 TODO"的正确读法是：

> 文档描述的是**目标合同**；`main` 上的代码是**现状**；两者之间的差额就是 TODO。

分支直接合入 main 会把 176 个无关文件一起带进去，**不能整体回迁**，需要只摘 `gitbook/` 部分。

---

## 1. TODO 现状核实（逐条对照代码，不是照抄文档）

我实际 grep/读了 `main` 的代码，不是只信文档自述。

| # | TODO 项 | 文档口径 | 我核实到的代码事实 | 判定 |
|---|---|---|---|---|
| 1 | 板身份/原点/两轴 tile | 未收口 | `NavTileGridConfig` 只有 `WidthChunks/HeightChunks/ChunkSizeCells/CellSizeCm/OriginXcm/OriginZcm`；**单板、无 boardId**、ChunkWidth==ChunkHeight（两轴同值） | 真实缺口 |
| 2 | 运行时档位/快照/代次 | 部分完成 | `NavTileStore` 有 `_revision` + `Replace()` 原子换入；但**无 generation 防旧覆盖、无独立 runtime tier** | 真实缺口 |
| 3 | 区域代价去 Cost | 残留待清 | `LogicTerrainCell` **仍有 `Cost` 字段**（`LogicTerrainField.cs:25`）；`NavMeshBakeConfig.NavAreaCostConfig` **仍有 `Cost`**（`NavMeshBakeConfig.cs:40`） | 真实缺口 |
| 3b | cost 合同分工 | 已定合同 | `assets/Navigation/pathing.json` 实际就是 `agentTypes[].navMesh.areaCosts[]`（实测读到 `areaId:0, cost:1.0`） | **合同已落地** |
| 3c | 纯 NavMesh 按 Agent 选策略 | 首 Agent 残留 | `AutoPathService` 有 `ResolveAgent(agentTypeId)` + 按 Agent 编译 `BuildAreaCosts`；但 Mesh 分支仍走 `_defaultAgent` 兜底 | 真实缺口，范围比文档描述小 |
| 3d | 水面水深/吃水 | 未收口 | 全仓 `grep RcConvexVolume\|sidecar` 在 `src/Core/Navigation/` **零命中**；`NavMeshLink` **全仓零命中** | 真实缺口（完全未实现） |
| 4 | 统一作者入口 `nav bake --map` | 未完成 | 文档自述 CLI/Bridge 共享部分链路已具备 | 部分缺口 |
| 5 | Manifest/冷启动 | 未收口 | `NavTileBinary.FormatVersion = 2` 且校验版本；但**无 manifest 文件**，冷启动加载链未证明 | 真实缺口 |
| 6 | 查询缓存/worker | 主线未收口 | PR #1164 仍 **OPEN draft**；`src/Core/Navigation` 无 `LoadedVersion` 共享缓存 | 真实缺口 |
| 7 | 旧类型退役 | 部分完成 | 命名已迁；`LogicTerrainCell.Cost` 旁路仍在 | 真实缺口 |
| 8 | Showcase 交付 | 已实现待验收 | registry 有 **7 个** navmesh showcase 全部 `status: active`；NavGate 有 `NavGate_ToggleFreeze` F 键并切换 `queue.ProcessingEnabled` | **入口真实存在** |

### 我额外发现的两个文档没写清的问题

1. **NavGate 的"过期时段等待"合同没有实现。** `NavGateMarchSystem.cs` 只在**完全无路径**时原地等待（注释：`无路径…代理原地等待——不做穿墙兜底`）。文档要求的是"冻结后单位在**不安全路段前等待**并显示导航已过期"。全仓在 NavGate 下 `grep 过期\|stale` **零命中**，HUD 没有过期指示。这是文档 UAT 场景 4 的真实阻塞点。
2. **两轴 tile 尺寸是假的。** `ChunkWidthCm` 与 `ChunkHeightCm` 都返回 `CellSizeCm * ChunkSizeCells`，无法表达 hex 板的非等比 tile（文档示例里 harbor 板 `44340×38400`）。#1346 的核心难点在这里。

---

## 2. 大里程碑拆分（5 个，按依赖顺序，非按文档 8 组原样照搬）

依赖关系是硬的：没有板身份就没法给产物命身份；没有产物身份就没法做冷启动；没有冷启动就没有可信 showcase。

```
M1 身份与源  ──►  M2 产物与冷启动  ──►  M4 运行时合同  ──►  M5 可玩验收
                        │
                        └──►  M3 语义与代价（可与 M2 部分并行）
```

### M1 — 板身份与源输入收敛（#1346、#1342、#1356、#1345）
- `NavTileGridConfig` 拆出 `boardId`，支持多板并存、每板独立 origin
- 两轴独立 tile 尺寸（真正支持 hex）
- `NavBakeSource` 角色化：`.height/.grid/.hex` 统一输入合同，bake 链脱离 `LogicTerrainField`
- 语义 sidecar 接入（水面/区域 → 凸多面体）

### M2 — 产物身份与冷启动（#403、#404、#1345、#1348）
- manifest：`sourceRevision / semanticRevision / obstacleRevision / buildHash / tileRevision`
- `.ntil` 单一权威，退役 `detourBase64` 双 payload
- 冷启动重新加载并继续查询（**同进程重读内存不算**）

### M3 — 语义与 Agent 代价正交（#372、#479、#467）
- 删除 `LogicTerrainCell.Cost` 与 `NavMeshBakeConfig.NavAreaCostConfig.Cost` 两个残留
- 地图只存 `areaId/tags`；cost 全量走 `PathingConfig.agentTypes[].navMesh.areaCosts[]`
- 修掉纯 NavMesh 路径的 `_defaultAgent` 兜底，按请求身份选策略
- **注意**：这条的合同本身已落地，剩下是清残留 + 补接线，工作量比文档描述的轻

### M4 — 运行时档位与查询基础设施（#1347、#1164、#1129）
- 独立 runtime tier（有界分辨率/瓦片边长/体素预算）
- 不可变 source/intent/obstacle 快照 + generation 防旧结果覆盖
- 从 #1164 draft 提取查询缓存/并行 worker，按当前 `NavTileStore` 版本合同重整
- 查询诊断：详细失败原因 + 作者修复动作

### M5 — 可玩 Showcase 收口（#1402、#767、#457、#451）
- NavGate 补齐"过期时段等待 + HUD 明确显示重烤已冻结"
- Agent Bridge 完整观察→驱动→验证闭环（两次 `/health` 且 `pumpCount` 增长）
- 各功能子场景（多层、双 profile、水面、Link）独立入口
- 截图/录屏 + 门户验收证据

### 不建议现在做的（明确排除）
- **NavMesh Link**：全仓零代码。文档自己标"尚未实现，不可玩"。它是独立能力，不该塞进上面任何里程碑。
- **水深/吃水**：零代码。依赖 M1 的 sidecar，至少排到 M3 之后。
- **整体回迁分支**：`codex/nav-perf-query-cache-rebake-workers`、`codex/nav-bake-policy`、`codex/nav-authoring-showcase`、`codex/nav-bake-visualization`、PR #1006。文档已经逐支判定"只提取片段，不整支回迁"，我核实了 main 的 `src/Core/Navigation` 与文档基线一致，这些分支的实现确实不在主线。

---

## 3. 你可以人工验收什么

分成三档，**从最容易到最有价值**。每档都给了具体动作。

### 档 A：文档交付本身（现在就能验，5 分钟）

```powershell
cd C:/001_AI/_navmesh_audit_20260908
# 1. 24 个专题是否齐全
(Get-ChildItem gitbook/navmesh-features/*.md).Count   # 实测 25（24 专题 + README）
# 2. SUMMARY 是否全挂上
Select-String -Path gitbook/SUMMARY.md -Pattern "navmesh-features" | Measure-Object | % Count  # 实测 25
# 3. 本地构建站点
python scripts/build-site.py
```
**验收点**：0 warning；24 个专题全部可点开；线上 `navmesh.html#doc/navmesh-ssot.md` 与本地一致。

**但请特别注意**：这个分支**没进 main**。所以档 A 现在验的是"文档写完了吗"，不是"功能做完了吗"。如果要让文档进主线，需要单独摘 `gitbook/` 目录提交，**不要 `git merge` 整个分支**（会带入 176 个无关 src 文件）。

### 档 B：现状基线核对（半天，不需要写代码）

这些是**"文档说的缺口是否真实存在"**的验证，用来确认 TODO 拆分没有虚构工作量：

| 动作 | 期望结果 |
|---|---|
| `grep -rn "Cost" src/Core/Navigation/Terrain/LogicTerrainField.cs` | 命中 `Cost` 属性 → M3 残留真实 |
| `grep -rn "Cost" src/Core/Navigation/NavMesh/Config/NavMeshBakeConfig.cs` | 命中 `Cost` → M3 残留真实 |
| `grep -rn "NavMeshLink" src/` | **零命中** → Link 未实现 |
| `grep -rn "RcConvexVolume\|sidecar" src/Core/Navigation/` | **零命中** → 语义 sidecar 未实现 |
| 查看 `NavTileGridConfig.cs` | 无 `boardId`、`ChunkWidthCm == ChunkHeightCm` → M1 真实 |
| 查看 `NavTileStore.cs` | 有 `_revision`/`Replace`，无 `generation` → M2/M4 真实 |
| GitHub 上核 issue 状态 | #281/#372/#399/#403/#404/#415/#419/#425/#433/#451/#457/#467/#479/#480/#481/#483/#594/#712/#1004/#1008/#1129/#1342/#1345/#1346/#1347/#1348/#1349/#1350/#1356 **全部 OPEN**；PR #1164 draft |

我已逐条跑过，**文档的状态标注是诚实的**——没有把未收口写成已完成。这点值得肯定。

### 档 C：真正有产品价值的人工验收（需要跑起来）

这是**唯一能证明"导航体系真能用"**的档位，也是当前最大的空白。

**C1. NavGate 主演示（最高优先级，也是文档承认没验的）**
```powershell
# 启动（需带 AgentBridgeMod）
# preset: nav_gate_raylib  →  showcase id: navmesh_runtime_gate_showcase
```
按 F 键切换冻结/恢复，观察：
- [ ] 落门后是否出现 dirty tile 提示
- [ ] 重烤完成后 revision 是否增长
- [ ] 小队是否**全部**抵达 B 营（文档自述此前 4 场景中 2 个失败，其中一个就是"未全部抵达"）
- [ ] 冻结后 HUD 是否明确显示"重烤已冻结"
- [ ] **冻结期间单位是否在不安全路段前等待** ← 我核实这条**代码未实现**，预计会失败，这正是 M5 的验收靶子

**C2. 双 profile 分道（#372 的核心承诺）**
步兵走陆地、航船走水面，互不干扰。当前水面语义零实现，预计无法通过 → M1/M3 靶子。

**C3. 多板互不覆盖（#1346 的核心承诺）**
grid 板 + hex 板同时加载，修改一块不影响另一块。当前 `NavTileGridConfig` 无 boardId，预计无法通过 → M1 靶子。

**C4. 编辑器保存后冷启动（#403/#404 核心承诺）**
改地形/障碍 → 保存 → 关闭 → 重启 → 仍能查询。当前无 manifest，预计失败 → M2 靶子。

**C5. cost 只改一个 Agent 不重烤几何（#372 正交性）**
改 infantry 的 areaCost，ship 的策略与几何产物不变。这条**合同和代码都已就位**，验收通过概率最高，**建议先验这条建立信心**。

---

## 4. 建议的推进顺序

1. **先做档 C5**（cost 正交，最可能通过）→ 确认已落地部分真的能跑
2. **再做档 B**（半天，锁定 TODO 真实性，避免按错误前提排期）
3. **摘文档进 main**（只摘 `gitbook/`，不整支 merge）
4. 按 M1 → M2 → M4 → M5 推进；M3 可与 M2 并行（工作量小）
5. NavMesh Link 与水深/吃水单独立项，不阻塞主线

---

## 5. 推进记录（本次会话已完成）

工作树：`C:/001_AI/_navmesh_audit_20260908`（分支 `codex/navmesh-ssot-feature-catalog`，detached @ 10892035e2）

| 提交 | 内容 | 验证 |
|---|---|---|
| `e6d3a89bfd` | 每板寻址：`NavQueryServiceKey` 加 boardId；`NavBoardTileGeometry`（两轴尺寸 + board origin + 瓦片范围）；修两个真实缺陷（负坐标钒到 0、LocalXcm 未减 board origin）；越界改 fail-closed | 新增 11 项合同测试 |
| `9019464271` | 产物路径带 board 身份；单板历史路径逐字不变，已烘焙产物不需迁移 | 新增 2 项路径测试 |
| `5bf7a3d28a` | 多板启动端到端验收（走真实 `GameEngine.LoadNavForMap`） | 新增 3 项，全通过 |

测试：导航集 92 → 106 全通过；全量 366 → 380 通过，仍为同样 4 个**与导航无关的既有失败**（SkiaSharp 引用、BehaviorKind 白名单、Teams 绑定、relationship 反向索引）。已用 `git stash` 在原始基线上二次确认这 4 个失败在我改动前就存在。

### M1 已收口

- board 身份进入查询与服务注册 key
- board origin 真正参与瓦片定位（此前 `NavTileGridConfig.OriginXcm` 只是死数据）
- 两轴 tile 尺寸不再摄成正方形
- 多板同局部坐标不互相覆盖（单元 + 端到端两层验证）
- 多板启用时产物路径隔离；单板保持兼容
- 越界查询和缺声明板 fail-fast

### M1 剩余（未做）

- hex 板的非等比尺寸尚未用真实 hex 地图跑通（目前用合成几何验证两轴不塌）
- 跨 board 连接交给 board graph，未开工
- 编辑器面板的板选择/局部坐标显示（属于作者面，不在 Core）

### 独立评审发现（2026-09-08）

用 `codex review`（gpt-6-astra，逐提交审）与 `claude --model opus -p` 分别审了 M1。
两边**独立收敛**到同样的三个问题；codex 另外找到一个两边都没先看到的 P1。

| 来源 | 问题 | 我的核实 | 处理 |
|---|---|---|---|
| opus B1 / codex P2 | `Fix64.ToInt()` 是算术右移（= `FloorToInt`），`LocateTile` 的 `cx--` 是双重 floor | 实测 `-1000cm/tile16000` 得 tile `-2`（应为 `-1`） | 已删；补负坐标精确 tile 回归 |
| opus B2 / codex P1 | 多板注册表下未作用域 `TryGetStore` 遍历取第一个匹配，字典序不定 => 随机串板且 fail-open | 逐行确认 | 改为抛异常；新增 `PrimaryBoardId`/`TryCreatePrimaryQuery`/`TryGetPrimaryStore` |
| opus B3 / codex P1 | 单板非零 origin 被静默丢弃（只有多板才填 geometry） | 确认：原代码单板走硬编 `origin=0` | 改为总是按 `NavTileGridConfig` 构造几何 |
| codex P1 | `TryFindPathCore` 把世界坐标直接喂 Detour（网格是 board-local） | 撤销修复后测试实测变 `NotReady`，确认是真缺陷 | 入口减 origin、出口加回 |
| codex P2 | `SanitizePathSegment` 非单射，`board.1` 与 `board_1` 撞同一路径 | 实测碰撞 | 转义时追加 FNV-1a 后缀 |

**我一开始判断错了的两个 P1，已自行推翻并修掉：**

- codex P1#1（写入端/读取端路径不一致）：我当时以「没有真实多板 navmesh 地图」为由归为后续。
  但契约不一致本身就是陷阱，且修复只需复用已有的 `ResolvePrimaryNavigationBoard`。
- codex P1#2（boardless 查询身份）：我误判为「缺设计合同」。实际仓库**已有**约定——
  `ToolMapConfigResolver.ResolvePrimaryNavigationBoard` 规定名为 `default` 的板优先、否则取第一块。
  不是缺合同，是我没找到既有基建。

**教训**：判「这属于后续范围」之前，必须先搜现有基建里有没有既定约定。这次两条都栽在没搜到
`ToolMapConfigResolver` 上。

### 仍然开放（属后续里程碑）

- hex 多板的真实非等比瓦片：`NavTileGridConfig.ChunkWidthCm == ChunkHeightCm` 恒等，
  表达式上喂不出 hex 的 44340×38400。`NavBoardTileGeometry` 支持两轴，但 config 表达不了。
- 跨 board 连接交 board graph；编辑器面板的板选择属作者面。

---

## 6. M2 推进记录（产物清单与冷启动）

| 提交 | 内容 | 验证 |
|---|---|---|
| `095397b865` | `NavTileManifest`（身份 + 源指纹 + 确定性 buildHash）+ Tool 写入 + 加载前校验 | 新增 9 项 |
| `110927acd0` | store 加载时按清单校验 checksum 与 tile 版本 | 新增 3 项 |

### 设计要点

- **确定性 buildHash**：`writtenUtc` 明确排除，瓦片枚举顺序归一化，
  所以相同输入必然得到相同值。测试锁住"写入时间不影响"与"顺序不影响"。
- **fail-closed**：schema 不兼容、formatVersion 不匹配、buildHash 与自身内容不符、
  瓦片不在清单、checksum 不符、tile 版本冲突——全部拒载并给出重新烘焙动作。
- **冷启动语义**：`GameEngine` 在建立 `NavTileStore` 之前校验 mapId/boardId，
  通过后作为 `CoreServiceKeys.NavTileManifest` 发布。

### 修掉的一个真实陷阱

`NavTileBinary.Write` 会对**序列化后的 payload** 重新计算 checksum，
而 `DefaultGridNavTileFactory` 造出的内存瓦片 `Checksum` 恒为 0。
写入端最初把内存值写进清单，会让**每个瓦片**加载时校验失败。
改为写盘后回读真实 checksum 再记录。

### 两个被仓库规则拦下的违规（已改）

- `NavigationSpatialScaleMagicNumberContractTests`：Tool 里的 `64 * 1024` 缓冲区
  被判为内联空间尺度魔数。改为具名常量。
- `ArchitectureGuardTests.Codebase_MustNotContainCompatibilityOrFallbackMarkers`：
  注释里写了 "backward compatibility"。仓库明令禁止该表述。
  实际设计意图不是兼容旧格式，而是"清单存在即绑定"，已改成如实描述。

### M2 剩余

- 编辑器 Artifact 面板读取清单并显示拒载原因（作者面）
- 五节点冷启动证据（写入前 / 写入后 / 退出 / 重读 / 继续操作）——属 M5 验收
- `.ntil` 与 `detourBase64` 双 payload 收敛（#404）未动
