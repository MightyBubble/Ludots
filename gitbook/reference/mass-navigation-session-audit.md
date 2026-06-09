# Mass Navigation 会话审计记录

> 快照时间：2026-06-07。
>
> 本文目的：把这次 mass-navigation / navmesh showcase 长会话里实际发生过的事情说清楚，包括 JSONL 证据、工作树位置、分支状态、实际改动范围、已完成内容和未验收风险。
>
> 这是一份审计和交接文档，不是验收通过报告。

## 1. 核心结论

这次 mass-navigation / navmesh showcase 的主要实现工作，不在原始工作树：

```text
C:\001_AI\LudotsProd
```

实际实现工作树是：

```text
C:\001_AI\LudotsProd_massnav_bake_data
```

对应分支：

```text
codex/mass-nav-bake-data-showcase
```

它跟踪的远端分支是：

```text
origin/codex/mass-nav-pr129-cleanup
```

当前 HEAD：

```text
dc6a5b2fadcc427df72f8b3eefe56a113b4d2527
```

HEAD 提交说明：

```text
feat(mass-navigation): add health drift and stronger terrain relief
```

`HEAD...@{u}` 结果是 `0 0`，说明当前分支提交点和远端跟踪分支一致。

但是，这个工作树现在是脏的。所有 mass-navigation / navmesh / showcase 的新增和修改，目前主要都是未提交改动。不能把它当成已经合入、已经干净、已经验收通过的状态。

最重要的一句话：

```text
当前成果存在，但没有进入可交付验收状态。
```

## 2. 已找到的 JSONL 证据

这次主会话的 JSONL 文件是：

```text
C:\Users\sietg\.codex\sessions\2026\05\10\rollout-2026-05-10T21-59-37-019e122f-b89a-7833-bb08-94c93ac44a9e.jsonl
```

该文件第一行元数据确认：

- thread id：`019e122f-b89a-7833-bb08-94c93ac44a9e`
- 会话启动 cwd：`C:\001_AI\LudotsProd`
- 来源：Codex Desktop / vscode

注意：`C:\Users\sietg\.codex\session_index.jsonl` 里还有一个更早的 `thread_name: massnav`，id 是 `019e0c1c-5946-7b70-9ff9-c5157ca46c0b`。本审计以主 JSONL 元数据和里面记录的 worktree 创建命令为准，不只依赖 session index 名称。

主 JSONL 里的关键证据：

- 第 142 行记录了 PR97 干净工作树创建：

```powershell
git worktree add --detach C:\001_AI\LudotsProd_pr97_clean origin/codex/issue-78-movement-acceptance
```

- 第 1108 行记录了 mass-navigation 实现工作树创建：

```powershell
git worktree add -b codex/mass-nav-bake-data-showcase C:\001_AI\LudotsProd_massnav_bake_data origin/codex/mass-nav-pr129-cleanup
```

- 同一个主 JSONL 后续 compacted history 里，也包含当前“回顾 session、整理工作树和分支”的审计请求。

找到的子线程 / subagent JSONL：

```text
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T13-07-24-019e24e1-e3fe-7a30-b5cc-c26a7e8b40bc.jsonl
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T13-07-24-019e24e1-e463-74b0-90e5-b7800db6fb3d.jsonl
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T13-07-24-019e24e1-e4c2-7b83-8f55-e6c23ce80756.jsonl
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T14-39-06-019e2535-d833-7332-9626-6c6b1f1373b9.jsonl
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T14-39-06-019e2535-d8a6-78b2-97f6-9925a5bee37d.jsonl
C:\Users\sietg\.codex\sessions\2026\05\14\rollout-2026-05-14T14-39-06-019e2535-d911-7c13-b56e-55ddb09cfc30.jsonl
```

这些文件第一行元数据确认它们的 parent thread 是：

```text
019e122f-b89a-7833-bb08-94c93ac44a9e
```

已知角色：

- Carver：探索 / 交叉分析
- Nash：探索 / 交叉分析
- Hubble：探索 / 交叉分析
- Leibniz：玩家视角审查 U01-U16
- Erdos：Mod 开发者 / SDK 视角审查 U01-U16 和正式链路
- Schrodinger：交互 / 性能视角审查

审计注意事项：

- 部分 JSONL 行非常大，包含 compacted history 或编码敏感内容。
- 之前有些 `ConvertFrom-Json` 尝试失败，不能把失败解析结果当证据。
- 本文只采用文件存在性、第一行元数据、字面 `Select-String` 命中和 Git 状态作为证据。

## 3. 工作树和分支状态

### 3.1 原始工作树

路径：

```text
C:\001_AI\LudotsProd
```

当前分支：

```text
codex/issue-128-entityinfo-contract-cleanup
```

观察到的状态：

- 跟踪 `origin/codex/issue-128-entityinfo-contract-cleanup`
- 落后远端 1 个提交
- 工作树也是脏的
- 包含其他 presentation / navigation / entity-info 相关改动
- 不能作为本次 mass-navigation showcase 的实际实现位置来判断

结论：

```text
C:\001_AI\LudotsProd 不是本次 massnav showcase 主实现工作树。
```

### 3.2 PR97 干净工作树

路径：

```text
C:\001_AI\LudotsProd_pr97_clean
```

当前状态：

```text
HEAD detached at 627e1ff0c79ccdd0e8b06080d4f53339d09c2791
```

来源：

```text
origin/codex/issue-78-movement-acceptance
```

用途：

- 查看 PR97
- 分析 PR97 和主线分叉情况
- 作为历史验收和 movement acceptance 参考

观察到的状态：

- detached HEAD
- 工作树干净
- 不是当前实现工作树

结论：

```text
PR97 只应作为证据和参考，不应整包合入当前 mass-navigation 栈。
```

### 3.3 当前 mass-navigation 实现工作树

路径：

```text
C:\001_AI\LudotsProd_massnav_bake_data
```

当前分支：

```text
codex/mass-nav-bake-data-showcase
```

跟踪远端：

```text
origin/codex/mass-nav-pr129-cleanup
```

HEAD：

```text
dc6a5b2fadcc427df72f8b3eefe56a113b4d2527
```

观察到的状态：

- 分支提交点和 upstream 对齐：`0 0`
- 分支创建后没有新提交
- tracked 修改 / 删除路径数：58
- untracked 文件数：1091
- 当前主要 mass-navigation / navmesh / showcase 改动都在这里

结论：

```text
这是当前 mass-navigation showcase 的实际工作树。
```

### 3.4 另一个 mass-navigation 相关工作树

路径：

```text
C:\001_AI\LudotsProd_massnav_pr129_cleanup
```

当前分支：

```text
codex/mass-nav-total-war-showcase
```

跟踪远端：

```text
origin/codex/mass-nav-total-war-showcase
```

观察到的状态：

- 工作树干净
- 是相关 mass-navigation 工作树
- 但不是当前 bake-data / showcase 脏改动所在位置

## 4. 时间线回顾

1. 会话从 `C:\001_AI\LudotsProd` 开始，最初任务是查看 Ludots 远端 PR 和分支，重点关注最近是否有 navmesh 改动。

2. 用户要求新建一个干净工作树，对齐远端代码，不带当前工作区改动，重点看 PR97 和主线分叉情况。

3. 创建了 PR97 干净工作树：

```text
C:\001_AI\LudotsProd_pr97_clean
```

4. PR97 被用于查看历史 movement acceptance 和分叉情况。结论倾向于把 PR97 当参考材料，而不是整包合并。

5. 讨论转向大型 RTS 导航体系设计：256 x 256 chunk、64km x 64km 世界、路网 graph、navmesh、多策略寻路、HPA、flowfield 避障、多 layer、多 cost、runtime 策略切换、debug 可视化和 Mod 开发者 UAT。

6. 之后生成 / 修改了 mass-navigation 验收文档：

```text
C:\001_AI\LudotsProd_massnav_bake_data\gitbook\reference\mass-navigation-showcase-acceptance.md
```

用户后来明确指出：这个文档在多轮修改中偏离了最初验收目标。本审计不再改写该验收文档。

7. 创建当前实现工作树：

```text
C:\001_AI\LudotsProd_massnav_bake_data
```

8. 实现方向先进入 navmesh / data bake 基建，重点包括 `LogicHeightmap` 统一数据源、Recast 烘焙和 Raylib bake 验证工具。

9. 工作继续扩大到 U01-U16 多个 showcase / 用例 mod、诊断数据、验收脚本、报告、截图、minimap、path-only、HPA、大世界、性能 debug 等。

10. 用户手动测试至少 U04 path-only 相关场景，反馈没有路线 / 曲线，debug 线条和表现也看不懂。

11. 后续定位到 U04 的 `RouteOnly` 模式没有画 route line，并做了局部修复。

12. 当前暂停功能推进，转为审计，因为现有工作范围过大、未提交、未完整验收。

## 5. 当前脏工作树里实际存在什么

以下内容都在：

```text
C:\001_AI\LudotsProd_massnav_bake_data
```

文档：

```text
gitbook/reference/mass-navigation-showcase-acceptance.md
gitbook/reference/mass-navigation-showcase-progress.md
gitbook/reference/mass-navigation-session-audit.md
```

showcase 结构：

```text
mods/showcases/mass_navigation/
```

脚本里规划的一用例一 mod 映射：

- U01：visual heightmap bake
- U02：logic heightmap bake
- U03：layer area editor
- U04：path-only query
- U05：world HPA route
- U06：strategy switch
- U07：order reuse
- U08：target allocation
- U09：layer costs
- U10：waypoint authoring
- U11：large world streaming
- U12：10k flow
- U13：static obstacle world
- U14：performance debug
- U15：debug visual budget
- U16：bake tool query

navmesh / bake 基建相关文件：

```text
src/Core/Navigation/NavMesh/Diagnostics/
src/Core/Navigation/NavMesh/LogicHeightmap/
src/Tools/Ludots.NavBake.Raylib/
src/Tools/Ludots.NavBake.Recast/RecastNavTileBaker.cs
src/Tools/Ludots.Tool/Program.cs
src/Tools/Ludots.Tool/LogicHeightmapFixtureGenerator.cs
src/Tools/Ludots.Tool/VisualHeightmapFixtureGenerator.cs
src/Core/Engine/GameEngine.cs
src/Core/Navigation/NavMesh/NavTileStore.cs
src/Core/Navigation/NavMesh/NavQueryService.cs
```

MassNavigationMod 诊断 / runtime / presentation 相关文件：

```text
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationAcceptanceDiagnostics.cs
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationBakeDataDiagnostics.cs
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationHpaGraphDiagnosticsBuilder.cs
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationRoadGraphDiagnostics.cs
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationShowcaseGuideRuntime.cs
mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationStaticObstacleWorldAsset.cs
mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationPathPreviewInputSystem.cs
mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationShowcasePresentationSystem.cs
mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationShowcaseReplaySystem.cs
```

验收 / 运行脚本：

```text
scripts/acceptance/run-mass-navigation-showcase-acceptance.ps1
scripts/acceptance/run-mass-navigation-usecase.ps1
scripts/acceptance/run-navmesh-bake-raylib-acceptance.ps1
```

观察到的证据目录：

```text
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\mass-navigation-usecases-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\mass-navigation-showcase-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\mass-navigation-large-world-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\navmesh-bake-raylib-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\navmesh-layer-editor-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\navmesh-visual-heightmap-current
C:\001_AI\LudotsProd_massnav_bake_data\artifacts\acceptance\navmesh-logic-heightmap-current
```

工具命令方向：

- `map to-lhtm --sourceKind vtxm/vhtm/react`
- `map gen-lhtm`
- `map patch-lhtm`
- `nav bake-recast-lhtm`

当前已实现或部分实现的设计方向：

- visual heightmap / vertex map / generated map source 统一收敛到 `LogicHeightmap`
- Recast navmesh bake 消费 `LogicHeightmap`
- Raylib bake 工具用于可视化和验证 bake 数据
- 多个 showcase 用例试图拆成独立入口

## 6. 最近 U04 修过什么

用户反馈 U04 path-only 场景看不到路线 / 曲线，只看到端点。

定位到的问题文件：

```text
mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationShowcasePresentationSystem.cs
```

问题本质：

```text
RouteOnly 可视模式跳过了 line 绘制，所以路线没有画出来。
```

当前脏工作树里的局部修复：

- 为 `RouteOnly` 增加可见 route band
- 使用 `AcceptanceDiagnostics.PathOnlyPathPoints` 作为路线来源
- 修改 presentation test，要求 U04 必须输出 line overlay
- 修改 `src/Core/Engine/GameEngine.cs` 里的 nav tile stride 加载逻辑，让 active-window baked tile 可以按真实 tile stride 被查询

handoff 记录里提到的 focused test：

```powershell
dotnet test src\Tests\PresentationTests\PresentationTests.csproj --filter "PathOnlyFocusedShowcase_ConsumesPlayerPickedEndpointsWithoutSubmittingOrders|MinimapInputConsumer_ProvidesConfirmAndCommandGroundOverridesForPathShowcase|GuidedShowcasePresentation_EmitsReadableScreenAndGroundDebugOverlays|FocusedEntryOverlay_UsesSmallBattlefieldStatusCapsule|FocusedEntryHudAndMinimap_ScaleFromViewportWithoutCoveringBattlefield|WaypointAuthoringFocusedShowcase_EditsAuthoredWaypointAndRegeneratesPathpointsWithoutOrder" -v:minimal
```

重要说明：

```text
这只能说明局部测试通过，不代表 U04 已经被用户验收通过。
```

## 7. 不能声称已经完成的内容

以下内容目前不能说已经完成，也不能说已经验收通过：

- U01-U16 全部用例没有完成人工验收。
- 每个用例一个干净 mod 的策略没有被证明达到生产质量。
- 每个玩法 / 编辑器 showcase 是否真的可操作、可理解，没有被逐个确认。
- 80 FPS / 100 FPS 目标没有在大规模场景下被完整证明。
- 10k+ 同屏 agent 的真实寻路和 flowfield 避障没有被完整验收。
- 40k+ 静态障碍物的 bake、加载、查询、solver active 链路没有被完整验收。
- 64km x 64km 世界尺度 navmesh bake 没有被完整验收。
- HPA 经过哪些 chunk、portal、corridor、route 的可视化没有被证明足够清楚。
- road graph / navmesh 策略切换没有被证明玩家可理解、mod 开发者可配置。
- 空军 / 水军 / 山地军 layer 和 cost policy 没有被完整验收。
- runtime navmesh / data 变更策略没有被完整验收。
- debug visual budget 是否真正不影响性能，没有被完整证明。
- 截图和 keyframe 不能替代可玩的玩法场景或可操作的编辑器场景。
- subagent review 可以作为辅助材料，但不能替代用户验收。

## 8. 主要风险

### 风险一：范围太大，而且没有提交

当前实现工作树里有：

```text
58 个 tracked 修改 / 删除路径
1091 个 untracked 文件
```

这已经不是一个可以直接 review 的小改动。

### 风险二：验收目标发生过漂移

用户明确指出 `mass-navigation-showcase-acceptance.md` 在多轮修改中偏离了最初目标。后续不应该继续通过改写验收文档来改变目标。

### 风险三：部分证据更像 smoke test，不是 UAT

脚本、截图、报告有价值，但不能等同于：

- 玩家能玩懂
- Mod 开发者能配置
- 编辑器用户能操作
- 性能目标真实达成
- 生产链路真的没有硬编码和假数据

### 风险四：改动跨越职责边界太多

当前改动涉及：

- Core Navigation
- Core Input
- Core Presentation / Minimap
- Raylib Adapter
- Launcher Evidence
- Tools
- MassNavigationMod
- Showcase Mods
- Tests
- Docs
- Generated Data

这带来很高的回归风险。

### 风险五：大量 untracked 数据会掩盖真正源码改动

1091 个 untracked 文件里包含大量生成数据、navtile、artifact、showcase asset。后续提交前必须拆分：

- 源码
- 配置
- 测试
- 文档
- 生成证据
- 不应入库的数据

## 9. 建议恢复方案

### 第一步：暂停继续铺新战线

在这份审计被确认前，不再继续扩展新的 navigation / showcase 需求。

### 第二步：先保护现场

在任何 reset、discard、清理、拆分之前，先做明确备份：

- 备份分支
- patch archive
- 或完整工作树快照

不能在没有用户明确同意的情况下对当前脏工作树运行破坏性 Git 操作。

### 第三步：把当前工作拆成可 review 单元

建议拆分顺序：

1. `LogicHeightmap` 数据格式和转换工具
2. Recast 从 `LogicHeightmap` bake navmesh
3. Raylib navmesh bake 可视化验证工具
4. U01 editor bake showcase
5. U04 path-only playable showcase
6. MassNavigation 共享 diagnostics
7. 大世界 / HPA 可视化
8. performance / flowfield 验收

### 第四步：一个用例一个用例验收

每个 case 必须满足：

- 独立、干净的 mod 入口
- 正式生产数据链路
- 没有假数据
- 没有硬编码糊弄
- 没有 fallback 管线
- 0 上下文同事可读 runbook
- 玩家玩法或编辑器操作能真实执行
- 有机器报告
- 有截图 / keyframe 作为辅助证据
- 性能相关 case 有性能门槛
- subagent review 只做辅助
- 最终必须用户验收

### 第五步：建议优先恢复哪个 case

如果优先恢复信任感和玩家可理解性，建议先做：

```text
U04 path-only query
```

如果优先恢复 navmesh bake 基建可信度，建议先做：

```text
U01 visual heightmap bake
```

从最近用户反馈看，U04 更适合作为第一个人工验收恢复点；从技术地基看，U01 更适合作为长期正确性恢复点。

## 10. 当前审计结论

本次工作不是完全没有产出，但现在状态不能叫完成。

当前真实工作树：

```text
C:\001_AI\LudotsProd_massnav_bake_data
```

当前真实分支：

```text
codex/mass-nav-bake-data-showcase
```

当前真实状态：

- 分支提交点和远端跟踪分支一致
- 但工作树大规模未提交
- 有基建和 showcase 脚手架
- 有部分 focused test 通过
- 有部分截图 / 报告 / evidence
- 没有完整用户验收
- 没有达到“所有 case pass”的交付标准

最终判断：

```text
需要停止扩张，保护现场，拆分改动，一个 case 一个 case 重新验收。
```
