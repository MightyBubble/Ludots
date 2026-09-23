# 10K MassNavigation：GPU、动画与回退审计

2026-09-10。状态：红蓝人替换和一项 GPU 优化已在独立实验中验证；整机回退审计尚未完成全部开关矩阵。本文不把渲染器探针换算成游戏 FPS。

## 版本与证据边界

审查源码固定为 `91618843a4be547611363e7971282fcc489ca9ee`。原工作区没有写入；47921 没有停止或清理；GAS 事务没有修改；没有提交或推送代码。

当前远端 main 已包含这个提交，因此现在执行 `origin/main...91618843a4` 得到空差分。历史 PR 比较使用合并提交第一父 `be8f0f69d2947613559f717d6e993631fb2533f4...91618843a4`，共有 146 个文件、6875 行增加、1095 行删除；这不是用另一个 MassNavigation 分支替换当前版本。见 [版本取证](revision-evidence.json) 和 [历史源码取证](history-evidence.json)。

早期 GPU 探针及第一次游戏采样基于 #1485 合并提交 `ffd11358f7929544c9f1f193b96aeb1eb28616d3`。它与指定提交还有一处贴地初始化判断及对应测试不同。发现差异后，实验工作树已切到 `codex/gpu-mannequin-91618843a4`，HEAD 为精确 `91618843a4`，重新构建并启动。当前运行的是该 HEAD 加未提交的模型、动画映射和 GPU 实验修改，不能作为未修改原版的 FPS。

取证运行使用端口 47931；启动清单及二进制/资源哈希见 [运行记录](launch/runs/20260909T184451.329584Z/process.json)。场景有 10000 个导航单位、持续生命 Effect、动画、血条和数字。Minimap 启动时开启，随后为观察人物切到关闭；相机也从全量远景改为中景，两个阶段不能混算性能。

## 近期远端修复核对

远端引用已于 2026-09-10 刷新。`origin/main` 为 `9b0048dd6f`；PR #1485 已合并，head `951f77bb6d`，merge `ffd11358f7`。实验工作树仍固定在 `91618843a4`，没有 merge 或 rebase 最新 main，因此没有执行 main 后续提交；本地 Presenter 改动只是在精确基线上独立补齐单体和批量路径。

| 问题 | 当前结论 | 远端覆盖与证据 |
|---|---|---|
| 普通单体 Presenter 首帧未采样后长期重复贴地检查 | **已进 main** | `951f77bb6d` 删除单体分类对瞬时 `Sampled` 的依赖。当前 main 为 `PresenterEntityRuntime.cs:1947-1964`；当前 10K 普通创建路径有 `behaviorTick=10000 -> 0` 的运行证据。 |
| 批量 Presenter 的同类问题 | **未进远端修复** | 当前 main `PresenterEntityRuntime.cs:2051-2061` 仍在第 2058 行以 `Sampled == 0` 决定长期 tick；PR #1486 第 2151 行和性能分支第 2065 行也保留。批量调用可由 `RuntimeEntitySpawnSystem.cs:1774 / MapLoader.cs:1252 -> CreateEntityAnchoredRootBatch:391 -> BatchRequiresGroundingTick` 到达。 |
| 本地单体与批量补齐 | **实验已通过，未提交** | 本地移除两处分类 guard；`PresenterGroundingAndGlobalEventTests` 37/37 通过，覆盖 pending/resolved/mixed、非零 `Grounding.Offset`、移动后高度、0 B 和 archetype 不变。它不是最新 main 的完整整合。 |
| `definition.PositionOffset` | **未解决** | 单体 helper 在 main 第 1952 行传 `definition:null`；批量分类虽检查第 1973 行，但 `PresenterBehaviorSystem.cs:2397-2409` 的跳过条件不检查定义偏移。独立红测中单体期望 2 次采样实际 1 次，批量期望 4 次实际 2 次，见 `tmp/rpc/presenter-position-offset-red.log`。X/Z 偏移必须按偏移后的落点采样；Y-only 的合同尚未定义。 |
| Presenter TransformSync 全量扫描 | **有实验优化，未进 main** | PR #1486 是 Draft，head `4416820854`；性能分支以 `f7c7c750d3` 独立复刻根 Presenter 标记。10K A/B 报告均值 `2.66 -> 2.10 ms`、峰值 `8.04 -> 6.43 ms`，可见实体和 20K HUD 数不变。该改动只减扫描，不修贴地或绘制。 |
| HUD 地形遮挡与 retained 位置更新 | **已进 main** | `85dfe3529f` 报告 headless 10K HUD 投影 `8.7-12.3 -> 2.0-2.3 ms`，20K 地形射线降到稳态约 0-1 次，0 B/frame。实验工作树未同步该提交。 |
| 拥挤 hard resolve 与出生/目标堆叠 | **已进 main** | `5657c69fbf`、`f1a971a3d7`、`de3dddd1bb` 把 10K hard resolve 报告值 `5.4 -> 约 1.0 ms`，穿透对 `42K -> 约 2.9K`，并让单位按唯一散点目标铺开。场景行为已改变，不能与 916 的中央聚堆帧直接只比 FPS。实验工作树未同步这些提交。 |
| flow 网格避障全邻域扫描 | **仅性能分支** | `origin/perf/massnav-10k-80fps@99133626ec` 的 `c407bd7d93` 使用按行阻塞格索引，提交报告 flow rebuild `11.5 -> 1.15 ms`。分支没有 PR，也未进 main；这是 flow 重建局部结果。 |
| Minimap 每 marker 的 knowledge 解析 | **仅性能分支，合同仍开口** | `7fe343843b`、`9dd1b14497` 把 resolver/tick 提到循环外，提交报告 `minimapProject` 均值降到 `1.92 ms`；集合级读取仍由 issue #1489 跟踪。分支没有 PR，也未进 main。 |
| GpuSkinned 姿势调色板容量 | **已解决并进 main** | #1485 的 `d5f2e23f79` 完成分页和显式超限；精确 916 已包含。 |
| 同一动画人物的细微重影或双提交 | **没有证据表明已解决** | `91618843a4..origin/main` 对 `PresenterEmitSystem.cs` 与 `RaylibGpuSkinnedBatchRenderer.cs` 无差分。main 仍按一个激活 SkinnedMesh slot 提交实例，主画面按 `model.meshCount` 逐部件绘制，阴影另走 depth pass；没有 StableId 防重、两路提交交集断言或视觉回归。 |
| 阴影体积裁剪 | **提交结论不能用于 GpuSkinned** | 性能分支 `7c366ab0f2` 声称裁掉 2.4K-4.2K skinned caster，但 `ContainsCaster` 只接入 leaf asset 和 typed static lane；GpuSkinned `TrySubmit` 成功后直接进入自己的 shadow flush。当前代码不能证明它减少了蒙皮阴影批次。 |
| PR #1488 Attachment 拓扑 | **与本问题无直接关系** | Draft head `1235dd15ed` 将 10K、128 层局部基准从 `8.11-14.22 ms` 降到 `0.49-0.54 ms`，0 alloc；它修改 `AttachmentPositionSyncSystem`，不处理 Presenter grounding、emit 或 GpuSkinned。 |

贴地失败处理也没有闭合：当前 main 在 heightmap 服务运行中消失时不会清掉动态 owner 旧的 `Sampled=1`（`TerrainHeightSyncSystem.cs:72-83`）；未声明 heightmap 的 static 对象仍会写 `Y=0` 并移除 pending（`:227-244`）；Presenter EveryFrame 采样失败仍会把高度写成 `Grounding.Offset`（`PresenterBehaviorSystem.cs:2268-2293`）。这些都是近期 PR 和性能分支未处理的现存行为。

本轮复盘结论：main 已解决普通 10K 单体 Presenter 的重复贴地检查，并吃到 HUD、拥挤 hard resolve 和场景铺开优化；本地实验补了批量 guard，但没有同步 main。批量 pending、`PositionOffset`、失败处理和动画重影仍未解决。性能分支的 TransformSync、flow、Minimap 可以分别进入独立审查，不能整包按“80 FPS”名称认定完成，也不能把局部毫秒数相加成整机收益。

## 按严重程度排列的发现

### P1：动画语义状态被当成文件里的动作序号

在精确提交中，`RaylibGpuSkinnedBatchRenderer.TrySubmit` 第 164 行传入 `stateToClipMap: null`。调用链是 `PresenterEmitSystem → SkinnedVisualBatchBuffer → RaylibPrimitiveRenderer.DrawSkinnedBatch → RaylibGpuSkinnedBatchRenderer.TrySubmit → RaylibSkinnedPlayback.ResolveFrame/MapStateToClipIndex`。骨骼挂点提供器也有相同漏接线。

每个可见蒙皮实例每帧解析一次，10K 时约 10000 次，复杂度 O(N)，本身不要求 GC 或 ECS 结构变更。越界会抛异常，但落在合法范围内的错误动作不会被发现：原士兵文件的 41/42 是 `Jump_Land/Jump_Start`，并非待机/行走。换成只有四个动作的早期 mannequin 后，42 直接越界。这是动画正确性缺陷，不是重复绘制已证实的原因。真正 GPU 骨骼接线的引入点是 `043577ca0d`（8 月 12 日）。

独立实验复用已有 AnimationProfileRegistry、AnimationClipRegistry 和 VFS，把语义状态 41/42 解析到文件动作。主画面与骨骼挂点使用同一份绑定；缺映射、错误文件、非法动作及配置失效明确报错。配置编译分配发生在启动期；10K 次稳定查询测试为 0 B。注册表定义对象仍允许原地改数组，此行为绕过 Revision；未发现当前运行期这样修改的调用。

可证伪 A/B：保持文件不变，以只有四动作的模型执行 41/42；原路径应抛越界，映射路径必须选到配置指定动作。再验证骨骼挂点与人物动作一致。18 项映射/挂点测试通过，见 [精确基线测试](../../../tmp/rpc/mapping-tests-916.log)；此前40项刚性附件/蒙皮验证见 [测试日志](measurements/skinning-tests.log)。

### P1：贴地初始化时机让 10K Presenter 持续重复执行贴地行为

精确提交 `PresenterEntityRuntime.cs:1958` 的 `ContinuousHeightmapSampleState.Sampled == 0` 判断会保留 Presenter 的贴地行为。对比合并版本，移除这一条件后由 TerrainHeightSyncSystem 负责实体贴地，pending 首次采样不会让全部 Presenter 保留每帧行为。

同一红蓝人配置的两次运行：合并基线 `behaviorTick=0, behavior≈0.01 ms`；精确基线持续 `behaviorTick=10000, behavior≈5.16–5.18 ms`。完整调用链、初始化与行为查询合同见 [Presentation 审查](presentation-audit.json)。执行量约 N 个行为/呈现帧；不是按帧增加组件的证据，常态 GC 仍需独立采样。

这是非交替的跨版本 A/B，不能把整帧时间直接相减。可证伪方法：在独立实验只切换该初始化条件，固定相机、模型、待机状态与采样窗口；记录 grounding 标记数、behaviorTick、地形查询次数及首次加载/玩家切换后的贴地正确性。当前证据支持约 4–6 ms CPU 候选预算，不保证 GPU 受限时同比增加 FPS。

证据：[合并基线日志](launch/runs/20260909T182222.511476Z/timing.log)、[精确基线实验日志](launch/runs/20260909T184451.329584Z/timing.log)。原判断来自 `d92aa08d69`（7月10日）；`951f77bb6d` 删除单实例 guard 后随合并进入 ffd，未混入精确提交。批量创建路径在 `PresenterEntityRuntime.cs:2059` 仍有同类 guard，不能把单行删除当成所有创建路径都已修好。

### P1：6 月候选版本与现在的 GPU 工作不等价

6 月 16 日候选 `2f6a8afb1f` 的 `RaylibPrimitiveRenderer.cs:480–519` 按 Animator 状态和 TRS 分桶，`1864–1886` 用普通 `DrawMeshInstanced`；没有采样动画或上传骨骼。它标记为 GpuSkinnedInstance，不能据此认为当时已经执行真正蒙皮。

8 月 12 日 `043577ca0d` 接入骨骼动画；8 月 30 日 `ca3cfc8371/7f89e524f9` 切换姿势纹理和对应阴影通道；9 月 6 日 `024653cf72` 才把方向光阴影图接到游戏宿主。当前每个 pass 都处理蒙皮后的几何。单位数 N、顶点数 V、pass 数 P 下，顶点工作规模约 O(NVP)；骨骼姿势共享并不让 GPU 只绘制一个人物。

用户所述 6 月约 60 FPS 的具体运行尚未唯一识别，不能把上述候选当成已经复现的基线。完整时间线、源码和模型哈希在 [历史取证](history-evidence.json)。

### P1 条件缺陷：复合障碍可能每个逻辑步重建全部流场

`MassNavigationEnvironmentBindingSystem.cs:96` 累加 PieceCount，`:60` 却与按实体计数的 AgentState.BlockerCount 比较；`MassNavigationAgentState.cs:67` 每实体只加一。只要一个障碍有两片，静态环境也会缓存失效，经 BindBlockers → ResetRuntimeObstaclesFromWorld → ForceFlowRebuild 重建代价和流场。`02073678b87`（6 月 21 日）引入计数单位差异，属于 #1485 保留问题。

默认地图五个 Circle 是单片，当前 10K 未证实触发。不能用它解释现有常态帧时间。完整频率、复杂度、GC、CommandBuffer 使用、64 障碍上限和 A/B 见 [MB-01](massnav-binding-audit.json)。A/B 使用同一几何的“一个两片障碍”和“两个单片障碍”，观察稳态重建次数。

### P1 尖峰风险：单个作者绑定变化可能迁移全体单位组件

`MassNavigationAuthoredAgentBindingSystem.cs:123/141/366 → MassNavigationSimulationRuntime.cs:631 → MassNavigationAgentState.cs:187 → BindSpawnedAgent:1053/1067/1076`。稳定帧不走该分支；删除、挂起或改变一个既有绑定后，可能全量 Remove/Add 运行时组件。按每实体四项估算，10K 变化帧可出现约 8 万次结构操作，实际次数需采样。

这不符合期望的稳定 SoA 槽位与延迟生命周期批处理。收集/排序 O(N log N)，结构迁移 O(N)；不能宣称常态每帧 GC。详见 [MB-02](massnav-binding-audit.json)。

### P2：导航、Presenter、HUD 的保留热路径仍待逐项 A/B

下表是已定位源码和复杂度的候选，不是已测根因。完整调用链、执行频率、GC/结构/容量行为与可证伪实验分别存于三份审查证据中。

| 路径 | 精确源码位置 | 当前工作 | 10K 相关性 |
|---|---|---|---|
| steering | MassNavigationFlowSolverState:1951–2048 | 扫描邻格全部 occupants，再逐单位扫描障碍，最后才判断部分保持静止条件 | 正常 O(N×局部密度+N×障碍数)，极密桶可能退化 O(N²)；MaxNeighbors 只限制保存邻居数 |
| flow | 同文件:1059–1097、1359–1417、1540–1605 | 每状态完整 cost copy 与网格 stencil；flow.enabled=false 只禁 crowd stamp | 100×100 网格，多个队/层重复；不能把开关名当“完全停算” |
| 导航快照/写回 | 同文件:947–997、1521–1538；MassNavigationFlowSolverEntitySync:10–58 | 每步复制 16N 字节、扫 N 个 dirty，写回 D 个实体 | 10K 每步约 160KB copy；O(N+D)，未证实是大头 |
| 路由执行 | MovePlanExecutionSystem:365/408；RouteExecutionSink:174/188/315/509/541 | 重复验证 owner，每次 Apply 重排路由、复制回滚路点 | O(R log R+ΣW)；R=10K、W=64 时路点 payload copy 5.12MB/Apply |
| 作者绑定 | AuthoredAgentBindingSystem:224/290 | 每逻辑步全量 hash，逐实体 TryGet EntityLayer | O(N)，已经使用 Chunk/Span，不能因未用某个 API 名称判错误 |
| 动画参数 | LocomotionAnimatorParamSystem:39/47/66 | 每可见 Presenter 解析 owner、查速度和参数 | 已有速度变化保护，不是无条件 Set；16 槽满时底层 Set 静默 return 是条件缺陷 |
| Presenter 同步/行为/输出 | 见 Presentation 审查 | owner 随机访问、事件分发及 retained/dirty 输出 | 要区分实体数据写入与结构变更、稳定缓存与容量增长 |
| HUD 投影 | WorldHudToScreenSystem:399–489 | owner 可见性缓存及到地形的射线 | 全投影时受 owner 数与射线成本影响；同 owner 的条/字不能一概计为两条射线 |

[Simulation 详细证据](massnav-simulation-audit.json) · [绑定/路由详细证据](massnav-binding-audit.json) · [Presentation 详细证据](presentation-audit.json)。

容量和分配另有明确条件：`PresenterBehaviorSystem.cs:1410` 的 Graph 参数正常执行路径会构造字符串，之后清寄存器数组；当前 MassNavigation 配置未见该绑定，不能算当前开销。SkinnedVisualBatchBuffer 是 AoS payload 每帧写入，但 Clear 只重置计数，不是清整块数组。HUD 缓存仅在最大实体 ID 超过容量时扩容；stamp 数组全清仅在计数回绕时发生。`WorldHudToScreenSystem.cs:303/356` 忽略 ScreenHud 插入失败，属于超限丢条目合同缺陷；当前采样 dropped=0，不是现有 FPS 的解释。

`ac8ab86af0` 已移除 hard resolve 的后备全扫并调整候选范围，本文没有重复整合。GAS #1463 已包含，局部 0.24 ms 不作为整帧结果。

### P2：重复阴影存在条件路径，当前细微重影仍未定因

`PresentationVisualProxyEmitter.cs:89` 写 skinned buffer，`:103` 还写普通 draw buffer；`RaylibHostLoop.cs:2941/2948`（精确提交，实验增加接线后为 2948/2955）先后提交两路阴影。普通 shadow 循环 `RaylibPrimitiveRenderer.cs:729–748` 没有排除 skinned，可能先画静态阴影再画动画阴影。

当前 Presenter 直接蒙皮输出会 continue，主画面普通 lane 也会跳过 skinned。实测全量帧 `skinnedRaw=10000, gpuSkinned=10000/6, primitiveRaw=6`，未见双倍人物实例计数。六个 mesh 对应身体部位，不是六个兵。尚未采集同一帧 StableId 的两路交集，也未证明当前细微重影触发上述 proxy 阴影路径。

A/B：逐帧记录普通 draw/skinned buffer 的 StableId 交集、主/阴影实际 draw，分别屏蔽交集项的静态阴影；固定角色动作逐帧比较轮廓。仅凭截图未看到重影不能判 bug 不存在。

## 已完成的 GPU A/B

AMD Radeon 8060S，OpenGL 3.3。1K/5K/10K 均使用同一机位、同一模型、配对同一动作帧；每规模 192 帧，前 64 帧暖机，每模式 64 个样本。没有运行 MassNavigation/HUD/Effect，因此下表是渲染器 GPU query。

共享骨骼 uniform（已进入独立试玩）：

| 单位 | 主 GPU 原/优化 ms | 阴影 GPU 原/优化 ms | 暖机后当前线程分配 |
|---:|---:|---:|---:|
| 1000 | 11.68 / 11.02 | 5.83 / 5.27 | 0 B |
| 5000 | 46.94 / 42.34 | 26.20 / 21.91 | 0 B |
| 10000 | 91.30 / 83.39 | 54.59 / 47.28 | 0 B |

三个规模同姿势截图逐像素一致。截图必须在 EndDrawing 交换缓冲前读取；此前交换后截图会读到前一动作帧，已经作废。优化只在整个已提交集合恰好一个姿势时使用 uniform，多姿势继续按既有纹理路径处理；不能推广到任意动作组合。原有姿势/实例容器仍存在扩容，因此不是所有冷启动/容量变化帧都 0Alloc。

预蒙皮共享顶点（仅 tmp 探针，未进入试玩）：

| 单位 | 主 GPU 原/实验 ms | 阴影 GPU 原/实验 ms | CPU 蒙皮+法线+上传 ms | 额外几何上传/帧 |
|---:|---:|---:|---:|---:|
| 1000 | 10.41 / 7.32 | 4.13 / 3.42 | 0.483 | 322344 B |
| 5000 | 46.40 / 33.29 | 21.90 / 15.58 | 0.450 | 322344 B |
| 10000 | 89.31 / 64.32 | 47.46 / 31.96 | 0.402 | 322344 B |

ABBA 顺序、同姿势、每帧同步。主/阴影各六次 draw，实例数与人口匹配；暖机后当前线程 0 B，Gen0/Gen1 都为 0。原生 UpdateModelAnimation 会把平移加进法线，本机 DLL 反汇编与数值检查确认；实验重新按不含平移的加权 3×3 计算法线，代价和额外上传已计入。

修正后 10K 三姿势外轮廓/背景掩码 XOR=0，但仍有 124–447 个像素 RGB 误差 >8，最大误差 206–224，主要在红蓝表面边界。它不是逐像素通过，尚缺顶点/深度证据及运动连续帧检查。混合 idle/walk 还需要每姿势独立存储、主/阴影期间不可覆盖、预分配容量与超限报错；不能把单个共享 VBO 方案直接用于多姿势。

见 [逐帧数据和汇总](measurements/summary.json)、[原生法线证据](../../../tmp/mannequin-preskin/normal-evidence/README.md)、[残差分析](../../../tmp/mannequin-preskin/normal-evidence/corrected-assessment.md)。本轮绝对 GPU 时间明显高于之前引用的 22.59/13.05 ms，不沿用旧窗口收益；目前未解释两次环境/负载差异。

## 模型与游戏运行

使用 KayKit 通用动画包的大号红蓝人。复用仓库 retarget_bake.py 烘焙真正 Idle_A 与 Walking_A；仍保留语义 41/42，文件动作映射为 5/6。来源、哈希和命令见 [素材清单](../../../tmp/mannequin-assets/build-manifest.json)。

原 knight 是 15 个 mesh、7024 顶点、6952 三角形；新文件六个 mesh、9067 顶点、9148 三角形。新角色部件更少，但三角形约多 31.6%；替换外形本身不能被算作 GPU 减负。早期 GPU 探针使用 mannequin_large_walk（8954 顶点），当前 idle/walk 重新导出的文件为 9067 顶点，二者必须分开记录。10K 新角色单 pass 约 9148 万三角形。

已经观察→下单→验证：单兵从队伍移出，Presenter 与 owner 位置一致，生命值从 233.762 变为 237.919，持续 Effect 仍为一个。订单已清空但最后位置并未精确等于目标，未把“移动发生”扩大成“抵达坐标完全通过”。这组移动证据来自较早合并基线，当前精确基线仍需完整移动验收。

当前精确基线实验全量远景的两个真实样本：

| 样本 UTC | 整帧 | Simulation | Presentation | Presenter行为 | HUD投影 | EndDrawing等待 |
|---|---:|---:|---:|---:|---:|---:|
| 18:46:57.368 | 123.61 | 10.68 | 26.13 | 5.18 | 14.49 | 61.92 |
| 18:47:04.783 | 134.12 | 2.81 | 22.64 | 5.16 | 14.71 | 83.54 |

单位均 ms，两个样本不是平均数。HUD 投影在 host post 阶段，不能重复加进 Presentation；EndDrawing 是 CPU 在帧末等待的时间，不能当成某个 GPU pass 的计时。约 0.6 ms 的 gpuSkinDraw 字段也是提交 CPU 时间，真实 GPU 要看独立 query。当前尚未达到 60 FPS。

## 回退时间线与行为差异

| 提交/时间 | 可核实行为 |
|---|---|
| ab0ff6b773，6/9 | showcase 候选，配置 1280 单位；不作为10K对照 |
| 2f6a8afb1f，6/16 | 10K候选；名称含 GpuSkinned，但普通实例路径没有真正骨骼更新 |
| 02073678b87，6/21 | 复合障碍改计片段数，实体计数比较未同步 |
| b6ccfc240c，7/9 | 去掉10K周期生命漂移，降低真实行为负载 |
| d6aed282edf，7/15 | 路由事务快照与排序加入 |
| 043577ca0d，8/12 | 游戏接入真正 GPU 蒙皮；语义状态映射仍漏接 |
| ca3cfc8371 / 7f89e524f9，8/30 | 改为姿势纹理，修复寻址并接入对应阴影 |
| 024653cf72，9/6 | 游戏宿主接入方向光阴影图 |
| d5f2e23f79，9/8 | 姿势调色板分页与计时 |
| 0261842da8，9/9 | 恢复持续生命 Effect |
| 0f1036ba53，9/9 | HUD 加地形遮挡 |
| ac8ab86af0，9/9 | 已整合 Presenter/MassNavigation 热路径改善 |
| 91618843a4，9/9 | 指定审查点，玩家切换回归测试 |

这些行为变化比最终 FPS 更能说明比较条件。三次历史 knight 资产哈希完全相同，未发现期间替换模型的证据。6 月具体流畅运行仍待唯一定位。

## 最小修复顺序与预算

1. 先保持精确基线、固定全量机位，并补齐逐呈现帧计量。导航 Last* 当前按固定逻辑步覆盖；最后一步无导航更新可写零，hard pair 还可能保留上次值。必须累计本帧全部逻辑步、标明各计数域，不能拿当前日志冒充完整逐帧 CSV。
2. 修正动画 profile 映射，完成初次贴地、玩家切换、idle/walk 交错验收。映射本身以正确性为目的，没有可承诺的帧预算。
3. 消除 pending 首采样导致的重复 Presenter 贴地。现有证据支持约 4–6 ms CPU 候选；需要只改该条件的配对 A/B。
4. 测 HUD 地形射线与 retained 投影；约 14–15 ms 是当前整个投影阶段上限，不是全部可释放预算。随后按候选邻居/障碍/flow/路由实际计数排序优化。静态审查不指定虚构毫秒数。
5. 保留已经通过画面一致性验证的单姿势 uniform 实验；本机该探针主+阴影约省15.2 ms，不能直接转成游戏收益。进一步共享顶点的候选约省40.5 ms GPU、增加约0.4 ms CPU，但画面与多姿势合同未通过，不列为已交付收益，也不能与前项简单相加。
6. 在更改生产路径前完成容量配置化。当前 renderer 数组/字典、HUD索引和姿势纹理仍有冷期/容量事件扩容；修复设计必须预分配、超限显式失败。保留 SoA、Chunk/Span、Inline Query 合同；生命周期通过稳定槽位与 CommandBuffer，不能用全量直接 Add/Remove 换取局部速度。

## 尚未完成的测量和剩余时间

| 要求 | 当前证据 | 状态 |
|---|---|---|
| 1K、5K、10K | GPU两类探针逐帧CSV | 渲染器已测；整机1K/5K未测 |
| 静止/移动、稀疏/拥挤 | 10K启动密集场景和单兵移动 | 没有完成四种固定场景配对 |
| HUD、地形遮挡、Animator、MassNavigation、Effect、Minimap开关 | 10K正常运行，Minimap语义开关已验证 | 完整逐项开关矩阵未测 |
| 总帧、Simulation、Presentation、Presenter、HUD | 游戏每60帧诊断样本 | 不是每帧无分配记录 |
| nav prep/steering/hard/flow/sync | Last*已有，但跨逻辑步覆盖 | 计量合同不足，禁止当帧总和 |
| Animator更新数、全部地形查询数、候选邻居数 | 部分计时/采样数存在 | 缺完整计数，不能用单位数填充 |
| draw/upload/GPU | GPU探针完整；游戏有CPU提交及upload日志 | 未把GPU query接到完整游戏所有pass |
| 当前线程分配、Gen0/Gen1 | 预蒙皮探针有逐帧，稳定为0 | 游戏整帧未测；不是所有工作线程分配 |
| 6月与916横向运行 | 已有祖先源码/行为/提交证据 | 6月流畅运行未唯一定位、未重跑 |

当前全量远景约124–134 ms样本中，帧末等待约62–84 ms尚未按GPU各pass解释；HUD约14.5 ms尚未拆出地形射线；其余CPU预算要在同一帧累计后才能计算残差。不能把探针GPU、游戏CPU提交、异帧Simulation和Presentation相加后称作已闭合预算。

## 验收记录

```gherkin
功能: 在10K导航场景观察通用红蓝人
  场景: 启动并观察角色与生命变化
    假如独立实验加载10000个导航单位
    当玩家观察队伍中的角色
    那么角色使用通用动画包的大号红蓝人
    而且头顶保留血条与生命数字
    而且持续生命效果仍能改变生命值

  场景: 区分待机与行走
    假如角色使用待机动作
    当玩家给角色下达导航移动命令
    那么角色发生位移并选择行走动作
    而且动作查找不应把语义状态当成文件序号
```

第一场景有截图、实体计数及较早运行生命变化证据；第二场景有映射测试和较早运行位移证据，精确基线的完整连续动作验收仍待补齐。截图未捕获细微重影不构成关闭该 bug 的依据。相关源码修改均留在独立工作树，未提交。
