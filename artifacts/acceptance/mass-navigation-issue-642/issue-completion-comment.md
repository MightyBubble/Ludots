# Issue #642 审计补充与完成说明

结论：issue 的问题判断总体成立，尤其是节拍双真相、配置职责聚合、地图/流式所有权、运行时可变引用和稳态容量边界。但原完成标准中有三项处方不应按字面实现，否则会让 MassNavigation 脱离 Ludots 的共享基础设施。

## 对原 issue 的补充

1. 不新增 MassNavigation 私有 JSON Schema 生成器。仓库正式配置 SSOT 是 `ConfigPipeline`、`ArrayById` catalog、`StrictJsonOptions`、`JsonRequired` 和强类型校验。此次将 MassNavigation 接入这条共享管线，并用架构测试锁定字段、catalog、map profile 和严格未知字段行为。新增子系统私有 schema 生成栈会形成第二套配置基础设施。
2. 不为没有现有产品需求的运行时热调参新建版本切换管线。地图 profile 在激活时编译为不可变 `MassNavigationRuntimePlan`，运行中修改原 authoring 对象不会影响当前 simulation。未来若出现真实热更新需求，应由共享配置版本/固定步切换机制承载，而不是由 MassNavigation 单独发明。
3. “60 秒托管分配为 0”必须拆分归因。Evidence host、Presentation 和进程基础设施仍有少量进程级分配，不能伪装成 MassNavigation 分配。当前正式合同是：求解器容量在冷阶段准备、fixed-step 超限 fail-fast、`AgentStorageAllocationCount` 稳态不增长；同时单独报告进程级 allocation/GC/heap/working-set。

原 issue 的 P2 厘米量化风险没有得到缺陷证据，因此未做数值域迁移。现有世界厘米坐标合同保持不变。

## 已完成

- 节拍只保留 `cadence.*Hz`，删除旧 interval-ticks 配置与 `MassNavigationFlowTuning`。
- `MassNavigationConfig.json` 改为 `ArrayById` capability profile；地图仅通过 `Metadata.massNavigation.profileId` 绑定。
- profile 分为 `runtime` 和可选 `sceneAuthoring`；核心 runtime 不再拥有场景生成、Presentation 或全局 team relationship。
- authoring 配置在地图激活时编译为不可变 plan；systems 通过稳定 `MassNavigationRuntimeBinding` 跟随当前 map runtime。
- Suspend/Resume 保留同一 map simulation，Unload 才释放；Formation、Road 和 LargeWorld 长驻系统具备动态 binding、map gate 和 `SuspendedTag` 隔离。
- GridBoard 继续拥有唯一 loaded-chunk 集合；MassNavigation、Road camera、route primer 使用独立 contributor lease，最终状态取并集，解绑不会清空其他子系统。
- solver、route sink 和 formation follower 的容量在冷阶段准备；fixed-step 不再首次扩容，超预算明确失败。
- Evidence recorder 和 UAT wrapper 现在使用同一真实字段合同，记录 timing-disabled 60 秒稳态、持续订单、GC/heap/working-set、HUD 完整性和 solver storage growth。
- 10K Presentation 容量按真实占用收敛，完整证明 `20000` world HUD、`10000` screen bars、`10000` screen texts、`0` drops。

## 验证

- `MassNavigationEvidenceContractTests`: `6/6`
- Presentation `MassNavigation` full gate: `146/146`
- Road showcase: `36/36`
- Route/Formation/map lifecycle 定向门: `8/8`
- LoadedChunks contracts: `13/13`，Road/Mass 集成 `5/5`，Mass streaming ownership `5/5`
- 最终 P0/P1 只读终审：CLEAR
- PowerShell parser、JSON、JSONL、文档链接与 `git diff --check`: PASS

另外执行了仓库级背景门禁，用于确认本议题边界：Architecture 在排除 `origin/main` 已存在的两条全仓旧词扫描失败后为 `142/142`；Gas 全量为 `1629` 通过、`1` 跳过、`105` 个非 MassNavigation 既有失败；Presentation 全量为 `614` 通过、`35` 个非 MassNavigation 既有失败。失败集中在旧 GAS/Performer/HUD/动画和 showcase 基准，相关代码路径不在本分支改动内；本议题的 MassNavigation、Road、Formation、loaded-chunk 与 evidence 门禁全部通过。

## 最终 10K 证据

Canonical isolated run: `20260711-081353`

- `10000` agents，初始订单/实际移动 `128/128`
- `60.0496s`，`2317` ticks，`12` sustained orders
- `38.5848 ticks/s`，平均 `24.2820ms`，最大 `297.3531ms`
- prepared capacity `10000 -> 10000`，agent-storage growth `0`
- process allocation `90.835KB/s`，full-GC retained growth `-214168B`，GC `0/0/0`
- managed heap start `1153128760B`，working set start `1889894400B`
- 相对最初基线：heap `-37.66%`，working set `-18.59%`，平均 tick `-10.70%`，throughput `+10.42%`

另一个 post-review final-stable run 为 `32.021 ticks/s / 28.580ms`。两次结果均保留在报告中；没有挑选历史最好跑分。进程仍约有 `1.153GB` managed heap 和 `1.890GB` working set，包含 10K agents、30009 performers 和 evidence host。进一步下降需要 performer/ECS 归因，不应再次盲目缩小共享容量。

## 证据路径

- `artifacts/acceptance/mass-navigation-issue-642/battle-report.md`
- `artifacts/acceptance/mass-navigation-issue-642/trace.jsonl`
- `artifacts/acceptance/mass-navigation-issue-642/path.mmd`
- `artifacts/acceptance/mass-navigation-issue-642/summary.json`
- `artifacts/acceptance/mass-navigation-issue-642/performance-comparison.md`
- `artifacts/doc-governance-report.md`
- `artifacts/techdebt/2026-07-11-mass-navigation-performance-evidence-truthfulness.md`
- `artifacts/techdebt/2026-07-11-massnav-config-domain-ownership.md`
- `artifacts/techdebt/2026-07-11-massnav-map-ownership.md`

实现已达到 issue 的架构、配置、容量和可复现验收目标。issue 可在对应实现合并后关闭。
