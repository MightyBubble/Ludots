# 正式寻路基建链路手册

`MassNavigationMod` 是正式的大世界寻路基建入口。它保留最高性能的 SoA massflow solver，并要求 gameplay 真相、选择、命令、表现、小地图都走 Ludots 正式链路。
这不是旧 `Navigation2DPlaygroundMod` 的延续，也不�?primitive draw demo。任何缺配置、缺服务、缺 template、缺 performer、小地图未注册、容量不足、board/world 不一致，都应�?fail-fast，而不�?fallback 到另一条路径�?

## 目标体验

从策划和玩家视角看，这就是一张标�?RTS 大地图：

- 世界尺寸由配置声明，当前验收世界�?`64km x 64km`�?
- 小地图显示完整世界坐标、镜头视窗、单�?marker �?debug layer�?
- 玩家可以框选单位，并右键命令到任意合法世界坐标�?
- 镜头移动只改变观察预算和优先工作区，不能重新生成单位、重置单位、重跑初始脚本�?
- 镜头外单位仍然存在并继续执行，只允许按配置降频或降低表现预算�?

## 正式链路

当前链路如下�?

```text
ConfigPipeline
  -> MassNavigationConfig.json
  -> Entities/templates.json
  -> Presentation/performers.json
  -> RuntimeEntitySpawnQueue
  -> RuntimeEntitySpawnReceiptQueue(channel=MassNavigationIds.RuntimeSpawnReceiptChannelId)
  -> MassNavigationAgentIndex / MassNavigationAgentProfile / MassNavigationBlockerProfile
  -> SelectionRuntime / SelectionSetKeys.LivePrimary
  -> OrderBuffer / massNavigationMove
  -> MassNavigationGroupRuntime
  -> MassFlowSimulationState(SoA solver cache)
  -> WorldPositionCm / PreviousWorldPositionCm / FacingDirection
  -> WorldToVisualSyncSystem / PerformerEntityTransformSyncSystem
  -> PerformerEntityRuntime / InstancedStaticMesh
  -> Performer MinimapMarker
  -> Core MinimapRuntime / Skia overlay
```

## 复用基建

- `ConfigPipeline`：加�?mod config、template、performer、game presentation capacity�?
- `RuntimeEntitySpawnQueue`：所�?MassNavigation agent、blocker、hotspot marker 都通过 template spawn，不允许手写 `world.Create(...)` 拼表现组件�?
- `RuntimeEntitySpawnReceiptQueue`：MassNavigation 只消费自己的 receipt channel，不把共享队列当私有队列�?
- `SelectionRuntime`：框选真相是 `SelectionSetKeys.LivePrimary`；`selection.live.primary` 只是 authoring 层的语义名，不是第二套集合，也不是兼容别名�?- `OrderBuffer`：右键移动进�?`massNavigationMove`，再绑定到底�?group command�?
- `Team` / `TeamEntityLookup` / `TeamManager`：队�?id、关系、避让语义来自现�?team/relationship 基建�?
- `WorldPositionCm` / `FacingDirection`：ECS 中的正式位置和朝向是 gameplay/presentation 交接点�?
- `PerformerEntityRuntime`：世界表现只�?performer，MassNavigation 不注�?private primitive renderer�?
- `MinimapRuntime`：小地图使用 core full-map preset �?performer `MinimapMarker`，MassNavigation 不维护第二套 minimap runtime�?
- Ludots UI：左侧调参和右上角诊�?HUD 都走正式 UI/presentation 服务�?

## 配置文件

主要配置位于 `mods/capabilities/navigation/MassNavigationMod/assets/`�?

- `MassNavigationConfig.json`：solver window、hotspot/debug landmark、obstacle、scenario、team relationship、flow、arrival、avoidance、crowd semantics、presentation id 引用。它通过 `ConfigPipeline` 合并加载，但不拥有世界尺寸�?- `Entities/templates.json`：agent、blocker、hotspot marker 的正�?entity template�?
- `Presentation/mesh_assets.json`：MassNavigation 自有 blocker、hotspot、selection overlay 的正�?Model mesh id�?
- `Presentation/host_assets.json`：MassNavigation 自有 mesh �?Raylib 后端文件绑定；缺 host sourceUri 必须 fail-fast�?
- `Presentation/performers.json`：agent、blocker、hotspot marker �?performer definition、Blacksmith 正式角色素材引用、selection overlay、minimap marker�?
- `game.json`：presentation capacity、runtime spawn queue capacity、minimap capacity、启�?map、Navigation2D 开关�?
- `Maps/mass_navigation.json`：board/world bounds �?map-owned `VisualHeightmapAsset`。`PrimaryBoard.WorldSizeSpec.Bounds` 是世界尺�?SSOT，MassNavigation runtime 只读它�?
- `assets/terrain/mass_navigation_large_world_relief.vhtm`：MassNavigation 专属 64km visual heightmap。不要跨 mod 引用 showcase 私有地形资产�?
- `GAS/order_types.json`：`massNavigationMove` 订单类型�?
`MassNavigationConfig.presentation.teams[]` 把动�?team id 映射�?style/template/performer id。示例里�?`azure`、`crimson`、`amber`、`emerald` 是配�?style，不�?C# 逻辑写死的队伍语义�?
配置 authoring 只允许用户记语义名，不要求记内部 handle�?
- performer `slot` 使用 `body`、`attachment`、`grounding`、`animator`、`minimap` �?canonical 名称；不要增�?`staticMinimap` 这类按用例起的别名�?- performer `paramKey` / `*ParamKey` 使用语义字符串；loader 编译�?`PerformerParamKeyRegistry` �?int�?- order type �?`orderTypes` 对象 key �?SSOT；省�?`orderTypeId` �?loader 通过 key 稳定分配，不依赖 JSON 顺序�?- order blackboard key �?validation graph 使用语义字符串或 `none`；运行时仍是 int�?
真实调参值继续写数字，例�?cm、Hz、ms、px、颜色、scale、capacity、priority �?queue size。这些不是魔�?ID�?
## Authoring 合约

一个可导航 MassNavigation 单位必须�?template 声明，并�?spawn receipt binding 时校验：

- `Team`
- `OrderBuffer`
- `SelectionSelectableTag`
- `SelectionSelectableState`
- `WorldPositionCm`
- `FacingDirection`
- `MassNavigationAgentTag`
- `MassNavigationControllable`

`WorldPositionCm` authoring 会通过正式 component registry 补齐 `PreviousWorldPositionCm`、`VisualTransform`、`CullState`；receipt binding 校验的是运行时实体最终具备这些组件，而不是要求用户在模板里重复写三遍�?
blocker 必须声明�?
- `WorldPositionCm`
- `MassNavigationBlocker`

hotspot/debug marker 必须声明�?
- `WorldPositionCm`
- `MassNavigationHotspotMarker`

缺任意正式组件或运行时补齐失败都�?fail-fast。不要在业务系统里临时补组件，也不要�?order �?selection 系统隐式补齐 MassNavigation 依赖�?
## 表现合约

MassNavigation agent、blocker、hotspot marker 必须通过 performer 创建�?

- agent/body 使用 Blacksmith 正式素材包的 `AssetBinding(SkinnedMesh, GpuSkinnedInstance)`，当前配置为 `blacksmith.worker.knight` + `blacksmith.worker.locomotion` + `blacksmith.worker.profile`�?
- `MassNavigationMod` 必须显式依赖 `PerformerBlacksmithShowcaseMod`，以复用�?`Knight.glb`、动画、mesh registry �?host asset 绑定；不要把素材复制�?MassNavigation，也不要用临�?OBJ/primitive 替代�?
- selection overlay、blocker、hotspot 使用 MassNavigation 自有 `Presentation/mesh_assets.json` + `Presentation/host_assets.json` 中声明的 Model mesh�?
- agent performer 必须声明 `Grounding` behavior，并使用 map-owned `VisualHeightmapAsset` 吸地�?
- blocker �?hotspot/debug marker 必须声明 `Grounding` behavior；静态对象可�?`updatePolicy=Once`�?
- selection overlay 使用独立 scoped performer，选中时创建、取消选中�?reset 时销毁；scope 来自 owner `PresentationStableId`，不�?solver array index。marker 生命周期应由通用 selection mutation -> presentation event -> performer rule 链路驱动，不�?MassNavigation 私有 system �?selection 状态�?- minimap marker 使用 performer `MinimapMarker` behavior�?
- 颜色、尺寸、marker size、selection overlay 是否可见，都来自 performer config �?performer param�?

禁止�?MassNavigation 中写 C# color switch、primitive draw loop、非 instanced fallback、按 `Team` 推断 marker 样式�?
禁止�?agent 主视觉改�?MassNavigation 临时 OBJ、内�?primitive 或任何未在配置中显式声明的素材�?
禁止�?visual heightmap 后退到平面或黑背景；�?`CoreServiceKeys.VisualHeightmap` 必须 fail-fast�?

## 小地图合�?

启动 MassNavigation map 时必须配�?core 小地图：

- 调用 core `MinimapRuntime.UseRtsFullMapPreset()`�?
- viewport 来自 board `WorldSizeSpec.Bounds`�?
- marker 来自 performer `MinimapMarker`，不�?MassNavigation �?entity 推断�?
- 点击/拖动小地图必须跳到对应绝对世界坐标，不能黑屏，不能夹到“最近可玩区域”�?
- camera rect、active chunks、solver window、flow work area �?debug 层应�?core minimap/overlay 能力；如�?core �?API，扩�?core，而不是在 MassNavigation 私建 runtime�?

## 大世界工作区语义

当前实现是单 solver window 的大世界 massflow showcase�?

- ECS/world 坐标是权威位置�?
- SoA grid 是高性能 solver cache，不是世界边界�?
- `FlowWorkArea` 由玩家镜头、最新命令目标、选中单位 bounds 共同决定�?
- `SolverWindow` 是当前执�?cache。它可以小于 `FlowWorkArea`，这是当前单 window 限制，不是玩家规则�?
- `LoadedChunks` �?`SpatialQueryService.SetLoadedChunks(...)` 复用 Ludots 世界流�?空间查询基建�?

镜头切走再切回时，原热点单位不应重新创建、不应重置初始阵型、不应重新跑 bootstrap，只允许因配置预算发�?tick/表现降级�?

## 命名与抽取边�?

`MassNavigationMod` 是正式寻路基建的当前承载 mod。它负责把配置、template spawn、selection、order、ECS 写回、performer、小地图、诊断 HUD 和 UAT evidence 串成一条可验收的正式链路。可复用能力按职责命名：

- `MassFlow`：高性能 SoA flow-field / crowd solver、flow rebuild、arrival、avoidance、cadence 等热路径能力�?
- `MassNavigation`：面�?gameplay 的通用导航集成层，负责 command/group/agent binding、selection/order adapter、ECS state sync �?runtime facade�?
- `MassNavigation`：面向玩法和 authoring 的正式导航集成层名称，不再作为旧实验场别名使用。
本阶段保持现有 runtime id、config 文件、component、system 和 asset id 稳定，避免把行为迁移和 API 拆分混在同一个变更里。

### 配置边界

| 当前配置 | 当前归属 | 后续归属 | 边界 |
| --- | --- | --- | --- |
| `assets/MassNavigationConfig.json` | MassNavigation | 拆分为 `MassFlow` tuning + `MassNavigation` scenario config | solver window、cadence、flow、arrival、avoidance、crowd semantics 可复用；scenario、teams、obstacles、hotspots、presentation id 是场景 authoring。 |
| `assets/GAS/order_types.json` | MassNavigation | `MassNavigation` order contract + 场景绑定 | `massNavigationMove` 证明正式 `OrderBuffer` 链路；通用 order contract 不应依赖具体示例资产名。 |
| `assets/Entities/templates.json` | MassNavigation | 场景 authoring | concrete agent/blocker/hotspot template 是场景输入；通用层只定义 required component contract。 |
| `assets/Presentation/performers.json` | MassNavigation | 场景 authoring | performer、style、marker 和视觉证明留在 mod 内；通用层只依赖正式 presentation handoff。 |
| `assets/Maps/mass_navigation.json` | MassNavigation | 场景 authoring | board/world bounds 仍由 map 持有；通用 runtime 只通过 core service 读取。 |

### 后续抽取映射

| 当前文件 | 后续目标 | 边界 |
| --- | --- | --- |
| `Runtime/MassFlowSimulationState.cs` | `MassFlow` solver state | SoA grid/cache、flow rebuild、obstacle cache、pair avoidance �?solver hot path；不得依赖场�?UI/表现�?|
| `Runtime/MassNavigationSimulationRuntime.cs` | `MassNavigation` runtime facade | agent/group/command/sync cadence/diagnostics 的可复用 runtime 外壳；只挂正�?core service�?|
| `Runtime/MassFlowTuning.cs` | `MassFlow` config | flow-field scheduler �?rebuild tuning�?|
| `Runtime/MassFlowArrivalTuning.cs` | `MassFlow` config | arrival tuning�?|
| `Runtime/MassFlowAvoidanceTuning.cs` | `MassFlow` config | pair/crowd avoidance tuning�?|
| `Runtime/MassNavigationCrowdSemantics.cs` | `MassNavigation` �?`MassFlow` config | team relationship �?navigation policy 的映射；gameplay relationship 来源仍是 core team 基建�?|
| `Runtime/MassNavigationGroupRuntime.cs` | `MassNavigation` groups | group command state �?formation target ownership�?|
| `Runtime/MassNavigationCommandRuntime.cs` | `MassNavigation` commands | command intent、lifecycle �?dirty state�?|
| `Runtime/MassNavigationAgentState.cs` | `MassNavigation` agent index | ECS entity �?solver agent �?binding，以�?selection/order metadata�?|
| `Runtime/MassNavigationAgentProfileConfig.cs` | 拆分 config | movement/mass profile schema 可通用；具体 profile authoring 留在 MassNavigation 配置内。 |
| `Runtime/MassNavigationAuthoringContract.cs` | 拆分 contract | required-component validation 可继续沉淀到 `MassNavigation`；performer id 与场景模板校验留在本 mod 内。 |
| `Systems/MassNavigationCommandApplySystem.cs` | `MassNavigation` system | 把通用 command 应用�?runtime group �?solver state�?|
| `Systems/MassNavigationOrderBridgeSystem.cs` | `MassNavigation` order adapter | 从正�?`OrderBuffer` 桥接到通用 mass-navigation command intent�?|
| `Systems/MassNavigationCommandBridgeSystem.cs` | MassNavigation 或薄 adapter | 玩家输入 command collection 先保持本 mod 内，除非形成可复用 input adapter。 |
| `Systems/MassNavigationSpawnReceiptBindingSystem.cs` | 拆分 system | spawn receipt 到 agent binding 可通用；template/profile/style 假设留在 MassNavigation authoring 内。 |
| `Systems/MassNavigationAgentMetadataSyncSystem.cs` | `MassNavigation` sync | ECS/team/profile metadata 同步�?runtime�?|
| `Systems/MassNavigationFormationSystem.cs` | `MassNavigation` formation | formation target allocation �?group arrangement；若依赖 UI/scenario 则留在壳内�?|
| `Systems/MassNavigationSelectionSyncSystem.cs` | `MassNavigation` selection adapter | 正式 `SelectionRuntime` �?agent selected flags 的桥接�?|
| `assets/Presentation/performers.json` selection rules | MassNavigation presentation authoring | Selection marker 由 performer rule 驱动 scoped performer。MassNavigation author marker definition 和 light/heavy rules，不拥有 marker 生命周期 system。 |
| `Systems/MassNavigationPanelPresentationSystem.cs` | MassNavigation | 调参 UI presentation。 |
| `Systems/MassNavigationHudPresentationSystem.cs` | MassNavigation | 诊断 HUD 和 evidence 可见性。 |
| `Systems/MassNavigationScenarioBootstrap.cs` | MassNavigation | scenario spawn authoring。 |
| `UI/MassNavigationPanelController.cs` | MassNavigation | 调参 UI 证明 runtime controls，不定义 core API。 |

## 禁止�?

- 不回�?`Navigation2DPlaygroundMod` 承载 MassNavigation 新需求�?
- 不注�?`MassNavigationPrimitivePresentationSystem`�?
- 不依�?`MinimapControlMod` �?MassNavigation 私有 minimap runtime�?
- 不引�?`PrimitiveDrawBuffer` 绘制 agent�?
- 不按 `MapEntity`、`Name`、`Team` 扫描推断 minimap marker�?
- 不在 C# �?team color switch、team name、mesh id、marker size、world size、capacity fallback�?
- 不在业务代码里手�?`VisualTransform.Scale` 作为 MassNavigation 表现来源�?
- 不把 `SelectionRuntime`、control group、formation 当成底层 `NavGroup` 真身�?- 不把 `Mass2D` �?crowd �?`nav mass`�?- 不把 `TryGet` �?fallback。`TryGet` 只能用于检查后抛明确异常或合法的“没有数据”分支�?- 不在 MassNavigation system 中硬编码 selection marker performer 创建/销毁。选中反馈如果�?performer，就必须�?performer rules、通用 batch create �?scoped cleanup�?
## 启动

常规 Raylib 启动�?

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib --build auto
```

preset 启动�?

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_navigation_raylib
```

启动后应该看到：

- 4 个动�?team 的大规模单位�?
- 左侧 Ludots UI 调参面板�?
- 右上角常驻诊�?HUD�?
- core 小地�?full-map 视图�?
- selection overlay 随框选变化�?
- 右键移动进入 `massNavigationMove`，单位不会到达后回初始点�?

## UAT

手动 UAT�?

- 启动 `mass_navigation` Raylib�?
- 框选一批单位，右键移动到当前战术视野内坐标，确认单位移动且 selection overlay 可见�?
- 右键移动到远处合法世界坐标，确认命令被接受，flow work area �?solver window 更新�?
- 在小地图点击中心、四角附近、空白区域，确认相机跳到绝对坐标且不黑屏�?
- 镜头切到远处再切回原热点，确认单位没有重新创建、没有重置、没有重新跑初始阵型�?
- Reset 场景后再次框选和右键，确�?group/order/selection 重新干净可用�?
- 检�?HUD �?FPS、frame、performer、minimap、mass_navigation timing 来自真实 timing diagnostics，不使用推断值冒充�?

自动证据 UAT�?

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib --record artifacts\acceptance\mass-navigation-large-world-rts
```

重复 soak�?

```powershell
.\scripts\acceptance\run-mass-navigation-large-world-uat.ps1 -Iterations 3 -OutputRoot artifacts\acceptance\mass-navigation-large-world-soak
```

通宵 soak�?

```powershell
.\scripts\acceptance\run-mass-navigation-large-world-uat.ps1 -Iterations 0 -UntilLocalTime 06:00 -OutputRoot artifacts\acceptance\mass-navigation-large-world-overnight -StopOnFailure
```

证据目录必须包含�?

- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `summary.json`
- `visible-checklist.md`
- `screens/timeline.png`

缺任一证据文件都是失败，不�?fallback success�?

## 验收矩阵

| 类别 | 验收�?|
| --- | --- |
| Build | `MassNavigationMod` Release build 通过�?|
| Static guard | MassNavigation 源码不引�?primitive renderer、旧 minimap control、硬编码 team style switch�?|
| Config contract | �?template、缺 performer、缺 board bounds、缺 team style、容量不足都 fail-fast�?|
| Selection/order | 框选走 `SelectionRuntime`，右键走 `OrderBuffer -> massNavigationMove -> group command`�?|
| Performer | agent/blocker/hotspot 全部通过 performer 创建�?transform sync�?|
| Minimap | marker 来自 `MinimapMarker`，全图坐标、小地图点击、camera rect 都正确�?|
| Behavior | 镜头切走再切回不会重�?spawn �?reset�?|
| Performance | 记录真实 `frame_ms`、`mass_navigation_ms`、`performer_ms`、`minimap_ms`、`presentation_ms`、warmup �?alloc�?|
| Soak | 长跑不出�?group/runtime 泄漏，reset 后性能恢复�?|

## 当前限制

- 当前 solver cache 仍是单窗�?SoA cache。多热点同时高精度仿真需要下一步扩展为�?solver window 或多分辨�?allocator�?
- `FlowWorkArea` 已经�?camera/command/selection 驱动，但 debug overlay 能力仍应继续沉淀�?core minimap/overlay API�?
- `HotZone` 字段当前更接�?known contact / debug landmark，后续应重命名，避免误导玩家和策划�?
- 自动 evidence 跑的�?headless 路径，不声明 live render FPS。实�?FPS �?Raylib HUD �?renderer benchmark 为准�?

## 读代码顺�?

1. `mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationRuntime.cs`
2. `mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationScenarioBootstrap.cs`
3. `mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationSpawnReceiptBindingSystem.cs`
4. `mods/capabilities/navigation/MassNavigationMod/Runtime/MassNavigationSimulationRuntime.cs`
5. `mods/capabilities/navigation/MassNavigationMod/Runtime/MassFlowSimulationState.cs`
6. `mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationCommandBridgeSystem.cs`
7. `mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationOrderBridgeSystem.cs`
8. `mods/capabilities/navigation/MassNavigationMod/Systems/MassNavigationFormationSystem.cs`
9. `mods/capabilities/navigation/MassNavigationMod/assets/Presentation/performers.json`
10. `mods/capabilities/navigation/MassNavigationMod/UI/MassNavigationPanelController.cs`

