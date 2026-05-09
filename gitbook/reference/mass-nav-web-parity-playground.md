# MassNavWebParity 正式链路手册

`MassNavWebParityMod` 是当前 massflow 方向的唯一 SSOT showcase。它保留最高性能的 SoA massflow solver，但 gameplay 真相、选择、命令、表现、小地图都必须走 Ludots 正式链路。

这不是旧 `Navigation2DPlaygroundMod` 的延续，也不是 primitive draw demo。任何缺配置、缺服务、缺 template、缺 performer、小地图未注册、容量不足、board/world 不一致，都应该 fail-fast，而不是 fallback 到另一条路径。

## 目标体验

从策划和玩家视角看，这就是一张标准 RTS 大地图：

- 世界尺寸由配置声明，当前验收世界是 `64km x 64km`。
- 小地图显示完整世界坐标、镜头视窗、单位 marker 和 debug layer。
- 玩家可以框选单位，并右键命令到任意合法世界坐标。
- 镜头移动只改变观察预算和优先工作区，不能重新生成单位、重置单位、重跑初始脚本。
- 镜头外单位仍然存在并继续执行，只允许按配置降频或降低表现预算。

## 正式链路

当前链路如下：

```text
ConfigPipeline
  -> MassNavWebParityConfig.json
  -> Entities/templates.json
  -> Presentation/performers.json
  -> RuntimeEntitySpawnQueue
  -> RuntimeEntitySpawnReceiptQueue(channel=MassNavWebParityIds.RuntimeSpawnReceiptChannelId)
  -> MassNavAgentIndex / MassNavAgentProfile / MassNavBlockerProfile
  -> SelectionRuntime / SelectionSetKeys.LivePrimary
  -> OrderBuffer / massNavMove
  -> MassNavGroupRuntime
  -> MassNavWebParitySimState(SoA solver cache)
  -> WorldPositionCm / PreviousWorldPositionCm / FacingDirection
  -> WorldToVisualSyncSystem / PerformerEntityTransformSyncSystem
  -> PerformerEntityRuntime / InstancedStaticMesh
  -> Performer MinimapMarker
  -> Core MinimapRuntime / Skia overlay
```

## 复用基建

- `ConfigPipeline`：加载 mod config、template、performer、game presentation capacity。
- `RuntimeEntitySpawnQueue`：所有 MassNav agent、blocker、hotspot marker 都通过 template spawn，不允许手写 `world.Create(...)` 拼表现组件。
- `RuntimeEntitySpawnReceiptQueue`：MassNav 只消费自己的 receipt channel，不把共享队列当私有队列。
- `SelectionRuntime`：框选真相是 `SelectionSetKeys.LivePrimary`，这是选择集合的正式 key，不是兼容别名。
- `OrderBuffer`：右键移动进入 `massNavMove`，再绑定到底层 group command。
- `Team` / `TeamEntityLookup` / `TeamManager`：队伍 id、关系、避让语义来自现有 team/relationship 基建。
- `WorldPositionCm` / `FacingDirection`：ECS 中的正式位置和朝向是 gameplay/presentation 交接点。
- `PerformerEntityRuntime`：世界表现只走 performer，MassNav 不注册 private primitive renderer。
- `MinimapRuntime`：小地图使用 core full-map preset 和 performer `MinimapMarker`，MassNav 不维护第二套 minimap runtime。
- Ludots UI：左侧调参和右上角诊断 HUD 都走正式 UI/presentation 服务。

## 配置文件

主要配置位于 `mods/showcases/navigation/MassNavWebParityMod/assets/`。

- `MassNavWebParityConfig.json`：solver window、hotspot/debug landmark、obstacle、scenario、team relationship、flow、arrival、avoidance、crowd semantics、presentation id 引用。它通过 `ConfigPipeline` 合并加载，但不拥有世界尺寸。
- `Entities/templates.json`：agent、blocker、hotspot marker 的正式 entity template。
- `Presentation/mesh_assets.json`：MassNav 自有 blocker、hotspot、selection overlay 的正式 Model mesh id。
- `Presentation/host_assets.json`：MassNav 自有 mesh 的 Raylib 后端文件绑定；缺 host sourceUri 必须 fail-fast。
- `Presentation/performers.json`：agent、blocker、hotspot marker 的 performer definition、Blacksmith 正式角色素材引用、selection overlay、minimap marker。
- `game.json`：presentation capacity、runtime spawn queue capacity、minimap capacity、启动 map、Navigation2D 开关。
- `Maps/mass_nav_web_parity.json`：board/world bounds 和 map-owned `VisualHeightmapAsset`。`PrimaryBoard.WorldSizeSpec.Bounds` 是世界尺寸 SSOT，MassNav runtime 只读它。
- `assets/terrain/mass_nav_web_parity_large_world_relief.vhtm`：MassNav 专属 64km visual heightmap。不要跨 mod 引用 showcase 私有地形资产。
- `GAS/order_types.json`：`massNavMove` 订单类型。

`MassNavWebParityConfig.presentation.teams[]` 把动态 team id 映射到 style/template/performer id。示例里的 `azure`、`crimson`、`amber`、`emerald` 是配置 style，不是 C# 逻辑写死的队伍语义。

配置 authoring 只允许用户记语义名，不要求记内部 handle：

- performer `slot` 使用 `body`、`attachment`、`grounding`、`animator`、`minimap` 等 canonical 名称；不要增加 `staticMinimap` 这类按用例起的别名。
- performer `paramKey` / `*ParamKey` 使用语义字符串；loader 编译到 `PerformerParamKeyRegistry` 的 int。
- order type 以 `orderTypes` 对象 key 为 SSOT；省略 `orderTypeId` 时 loader 通过 key 稳定分配，不依赖 JSON 顺序。
- order blackboard key 与 validation graph 使用语义字符串或 `none`；运行时仍是 int。

真实调参值继续写数字，例如 cm、Hz、ms、px、颜色、scale、capacity、priority 和 queue size。这些不是魔法 ID。

## Authoring 合约

一个可导航 MassNav 单位必须由 template 声明，并在 spawn receipt binding 时校验：

- `Team`
- `OrderBuffer`
- `SelectionSelectableTag`
- `SelectionSelectableState`
- `WorldPositionCm`
- `FacingDirection`
- `MassNavAgentTag`
- `MassNavControllable`

`WorldPositionCm` authoring 会通过正式 component registry 补齐 `PreviousWorldPositionCm`、`VisualTransform`、`CullState`；receipt binding 校验的是运行时实体最终具备这些组件，而不是要求用户在模板里重复写三遍。

blocker 必须声明：

- `WorldPositionCm`
- `MassNavBlocker`

hotspot/debug marker 必须声明：

- `WorldPositionCm`
- `MassNavHotspotMarker`

缺任意正式组件或运行时补齐失败都应 fail-fast。不要在业务系统里临时补组件，也不要让 order 或 selection 系统隐式补齐 MassNav 依赖。

## 表现合约

MassNav agent、blocker、hotspot marker 必须通过 performer 创建：

- agent/body 使用 Blacksmith 正式素材包的 `AssetBinding(SkinnedMesh, GpuSkinnedInstance)`，当前配置为 `blacksmith.worker.knight` + `blacksmith.worker.locomotion` + `blacksmith.worker.profile`。
- `MassNavWebParityMod` 必须显式依赖 `PerformerBlacksmithShowcaseMod`，以复用其 `Knight.glb`、动画、mesh registry 与 host asset 绑定；不要把素材复制到 MassNav，也不要用临时 OBJ/primitive 替代。
- selection overlay、blocker、hotspot 使用 MassNav 自有 `Presentation/mesh_assets.json` + `Presentation/host_assets.json` 中声明的 Model mesh。
- agent performer 必须声明 `Grounding` behavior，并使用 map-owned `VisualHeightmapAsset` 吸地。
- blocker 和 hotspot/debug marker 必须声明 `Grounding` behavior；静态对象可以 `updatePolicy=Once`。
- selection overlay 使用独立 scoped performer，选中时创建、取消选中或 reset 时销毁；scope 来自 owner `PresentationStableId`，不是 solver array index。marker 生命周期应由通用 selection mutation -> presentation event -> performer rule 链路驱动，不由 MassNav 私有 system 扫 selection 状态。
- minimap marker 使用 performer `MinimapMarker` behavior。
- 颜色、尺寸、marker size、selection overlay 是否可见，都来自 performer config 或 performer param。

禁止在 MassNav 中写 C# color switch、primitive draw loop、非 instanced fallback、按 `Team` 推断 marker 样式。
禁止把 agent 主视觉改回 MassNav 临时 OBJ、内置 primitive 或任何未在配置中显式声明的素材。
禁止缺 visual heightmap 后退到平面或黑背景；缺 `CoreServiceKeys.VisualHeightmap` 必须 fail-fast。

## 小地图合约

启动 MassNav map 时必须配置 core 小地图：

- 调用 core `MinimapRuntime.UseRtsFullMapPreset()`。
- viewport 来自 board `WorldSizeSpec.Bounds`。
- marker 来自 performer `MinimapMarker`，不是 MassNav 扫 entity 推断。
- 点击/拖动小地图必须跳到对应绝对世界坐标，不能黑屏，不能夹到“最近可玩区域”。
- camera rect、active chunks、solver window、flow work area 等 debug 层应接 core minimap/overlay 能力；如果 core 缺 API，扩展 core，而不是在 MassNav 私建 runtime。

## 大世界工作区语义

当前实现是单 solver window 的大世界 massflow showcase：

- ECS/world 坐标是权威位置。
- SoA grid 是高性能 solver cache，不是世界边界。
- `FlowWorkArea` 由玩家镜头、最新命令目标、选中单位 bounds 共同决定。
- `SolverWindow` 是当前执行 cache。它可以小于 `FlowWorkArea`，这是当前单 window 限制，不是玩家规则。
- `LoadedChunks` 和 `SpatialQueryService.SetLoadedChunks(...)` 复用 Ludots 世界流送/空间查询基建。

镜头切走再切回时，原热点单位不应重新创建、不应重置初始阵型、不应重新跑 bootstrap，只允许因配置预算发生 tick/表现降级。

## 命名与抽取边界

`MassNavWebParityMod` 是验收 showcase 壳，不是通用能力的最终命名。它负责证明正式链路完整可用：配置、template spawn、selection、order、ECS 写回、performer、小地图、诊断 HUD 和 UAT evidence。后续可复用能力按职责命名：

- `MassFlow`：高性能 SoA flow-field / crowd solver、flow rebuild、arrival、avoidance、cadence 等热路径能力。
- `MassNavigation`：面向 gameplay 的通用导航集成层，负责 command/group/agent binding、selection/order adapter、ECS state sync 和 runtime facade。
- `MassNavWebParity`：验收壳、场景 authoring、表现证明、调参 UI 和 evidence harness。

本阶段不重命名现有 runtime id、config 文件、component、system 或 asset id。现有 `MassNav*` 命名在抽 core 前保持稳定，避免把验收清理和行为迁移混在同一个变更里。

### 配置边界

| 当前配置 | 当前归属 | 后续归属 | 边界 |
| --- | --- | --- | --- |
| `assets/MassNavWebParityConfig.json` | showcase 壳 | 拆分为 `MassFlow` tuning + showcase scenario config | solver window、cadence、flow、arrival、avoidance、crowd semantics 可复用；scenario、teams、obstacles、hotspots、presentation id 是验收 authoring。 |
| `assets/GAS/order_types.json` | showcase 壳 | `MassNavigation` order contract + showcase binding | `massNavMove` 证明正式 `OrderBuffer` 链路；通用 order contract 不应依赖 WebParity 场景。 |
| `assets/Entities/templates.json` | showcase 壳 | showcase 壳 | concrete agent/blocker/hotspot template 是验收 authoring；通用层只定义 required component contract。 |
| `assets/Presentation/performers.json` | showcase 壳 | showcase 壳 | performer、style、marker 和视觉证明留在 mod 内；通用层只依赖正式 presentation handoff。 |
| `assets/Maps/mass_nav_web_parity.json` | showcase 壳 | showcase 壳 | board/world bounds 仍由 map 持有；通用 runtime 只通过 core service 读取。 |

### 后续抽取映射

| 当前文件 | 后续目标 | 边界 |
| --- | --- | --- |
| `Runtime/MassNavWebParitySimState.cs` | `MassFlow` solver state | SoA grid/cache、flow rebuild、obstacle cache、pair avoidance 和 solver hot path；不得依赖 showcase UI/表现。 |
| `Runtime/MassNavSimulationRuntime.cs` | `MassNavigation` runtime facade | agent/group/command/sync cadence/diagnostics 的可复用 runtime 外壳；只挂正式 core service。 |
| `Runtime/MassNavFlowTuning.cs` | `MassFlow` config | flow-field scheduler 和 rebuild tuning。 |
| `Runtime/MassNavArrivalTuning.cs` | `MassFlow` config | arrival tuning。 |
| `Runtime/MassNavAvoidanceTuning.cs` | `MassFlow` config | pair/crowd avoidance tuning。 |
| `Runtime/MassNavCrowdSemantics.cs` | `MassNavigation` 或 `MassFlow` config | team relationship 到 navigation policy 的映射；gameplay relationship 来源仍是 core team 基建。 |
| `Runtime/MassNavGroupRuntime.cs` | `MassNavigation` groups | group command state 和 formation target ownership。 |
| `Runtime/MassNavCommandRuntime.cs` | `MassNavigation` commands | command intent、lifecycle 和 dirty state。 |
| `Runtime/MassNavAgentState.cs` | `MassNavigation` agent index | ECS entity 到 solver agent 的 binding，以及 selection/order metadata。 |
| `Runtime/MassNavAgentProfileConfig.cs` | 拆分 config | movement/mass profile schema 可通用；具体 profile authoring 留在 showcase。 |
| `Runtime/MassNavAuthoringContract.cs` | 拆分 contract | required-component validation 可进入 `MassNavigation`；performer id 与 showcase template 校验留在壳内。 |
| `Systems/MassNavCommandApplySystem.cs` | `MassNavigation` system | 把通用 command 应用到 runtime group 和 solver state。 |
| `Systems/MassNavOrderBridgeSystem.cs` | `MassNavigation` order adapter | 从正式 `OrderBuffer` 桥接到通用 mass-navigation command intent。 |
| `Systems/MassNavCommandBridgeSystem.cs` | showcase 或薄 adapter | 玩家输入 command collection 先保持壳内，除非形成可复用 input adapter。 |
| `Systems/MassNavSpawnReceiptBindingSystem.cs` | 拆分 system | spawn receipt 到 agent binding 可通用；template/profile/style 假设留在 showcase。 |
| `Systems/MassNavAgentMetadataSyncSystem.cs` | `MassNavigation` sync | ECS/team/profile metadata 同步到 runtime。 |
| `Systems/MassNavFormationSystem.cs` | `MassNavigation` formation | formation target allocation 和 group arrangement；若依赖 UI/scenario 则留在壳内。 |
| `Systems/MassNavSelectionSyncSystem.cs` | `MassNavigation` selection adapter | 正式 `SelectionRuntime` 到 agent selected flags 的桥接。 |
| `Systems/MassNavSelectionPerformerSyncSystem.cs` | 待移除的 showcase presentation adapter | 当前仍承担 selection overlay 生命周期，属于过渡实现。目标是迁移到通用 selection mutation -> performer rule 链路，MassNav 只保留 performer 配置。 |
| `Systems/MassNavPanelPresentationSystem.cs` | showcase 壳 | 调参 UI presentation。 |
| `Systems/MassNavHudPresentationSystem.cs` | showcase 壳 | 诊断 HUD 和 evidence 可见性。 |
| `Systems/MassNavScenarioBootstrap.cs` | showcase 壳 | scenario spawn authoring。 |
| `UI/MassNavPlaygroundPanelController.cs` | showcase 壳 | 调参 UI 证明 runtime controls，不定义 core API。 |

## 禁止项

- 不回到 `Navigation2DPlaygroundMod` 承载 MassNav 新需求。
- 不注册 `MassNavPrimitivePresentationSystem`。
- 不依赖 `MinimapControlMod` 或 MassNav 私有 minimap runtime。
- 不引用 `PrimitiveDrawBuffer` 绘制 agent。
- 不按 `MapEntity`、`Name`、`Team` 扫描推断 minimap marker。
- 不在 C# 写 team color switch、team name、mesh id、marker size、world size、capacity fallback。
- 不在业务代码里手写 `VisualTransform.Scale` 作为 MassNav 表现来源。
- 不把 `SelectionRuntime`、control group、formation 当成底层 `NavGroup` 真身。
- 不把 `Mass2D` 当 crowd 的 `nav mass`。
- 不把 `TryGet` 当 fallback。`TryGet` 只能用于检查后抛明确异常或合法的“没有数据”分支。
- 不在 MassNav system 中硬编码 selection marker performer 创建/销毁。选中反馈如果是 performer，就必须走 performer rules、通用 batch create 和 scoped cleanup。

## 启动

常规 Raylib 启动：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity --adapter raylib --build auto
```

preset 启动：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity_raylib
```

启动后应该看到：

- 4 个动态 team 的大规模单位。
- 左侧 Ludots UI 调参面板。
- 右上角常驻诊断 HUD。
- core 小地图 full-map 视图。
- selection overlay 随框选变化。
- 右键移动进入 `massNavMove`，单位不会到达后回初始点。

## UAT

手动 UAT：

- 启动 `mass_nav_web_parity` Raylib。
- 框选一批单位，右键移动到当前战术视野内坐标，确认单位移动且 selection overlay 可见。
- 右键移动到远处合法世界坐标，确认命令被接受，flow work area 和 solver window 更新。
- 在小地图点击中心、四角附近、空白区域，确认相机跳到绝对坐标且不黑屏。
- 镜头切到远处再切回原热点，确认单位没有重新创建、没有重置、没有重新跑初始阵型。
- Reset 场景后再次框选和右键，确认 group/order/selection 重新干净可用。
- 检查 HUD 的 FPS、frame、performer、minimap、massnav timing 来自真实 timing diagnostics，不使用推断值冒充。

自动证据 UAT：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity --adapter raylib --record artifacts\acceptance\mass-nav-web-parity-large-world-rts
```

重复 soak：

```powershell
.\scripts\acceptance\run-mass-nav-web-parity-large-world-uat.ps1 -Iterations 3 -OutputRoot artifacts\acceptance\mass-nav-web-parity-large-world-soak
```

通宵 soak：

```powershell
.\scripts\acceptance\run-mass-nav-web-parity-large-world-uat.ps1 -Iterations 0 -UntilLocalTime 06:00 -OutputRoot artifacts\acceptance\mass-nav-web-parity-large-world-overnight -StopOnFailure
```

证据目录必须包含：

- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `summary.json`
- `visible-checklist.md`
- `screens/timeline.png`

缺任一证据文件都是失败，不是 fallback success。

## 验收矩阵

| 类别 | 验收点 |
| --- | --- |
| Build | `MassNavWebParityMod` Release build 通过。 |
| Static guard | MassNav 源码不引用 primitive renderer、旧 minimap control、硬编码 team style switch。 |
| Config contract | 缺 template、缺 performer、缺 board bounds、缺 team style、容量不足都 fail-fast。 |
| Selection/order | 框选走 `SelectionRuntime`，右键走 `OrderBuffer -> massNavMove -> group command`。 |
| Performer | agent/blocker/hotspot 全部通过 performer 创建和 transform sync。 |
| Minimap | marker 来自 `MinimapMarker`，全图坐标、小地图点击、camera rect 都正确。 |
| Behavior | 镜头切走再切回不会重新 spawn 或 reset。 |
| Performance | 记录真实 `frame_ms`、`massnav_ms`、`performer_ms`、`minimap_ms`、`presentation_ms`、warmup 后 alloc。 |
| Soak | 长跑不出现 group/runtime 泄漏，reset 后性能恢复。 |

## 当前限制

- 当前 solver cache 仍是单窗口 SoA cache。多热点同时高精度仿真需要下一步扩展为多 solver window 或多分辨率 allocator。
- `FlowWorkArea` 已经由 camera/command/selection 驱动，但 debug overlay 能力仍应继续沉淀到 core minimap/overlay API。
- `HotZone` 字段当前更接近 known contact / debug landmark，后续应重命名，避免误导玩家和策划。
- 自动 evidence 跑的是 headless 路径，不声明 live render FPS。实时 FPS 以 Raylib HUD 或 renderer benchmark 为准。

## 读代码顺序

1. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavPlaygroundRuntime.cs`
2. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavScenarioBootstrap.cs`
3. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavSpawnReceiptBindingSystem.cs`
4. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavSimulationRuntime.cs`
5. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavWebParitySimState.cs`
6. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavCommandBridgeSystem.cs`
7. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavOrderBridgeSystem.cs`
8. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavFormationSystem.cs`
9. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavSelectionPerformerSyncSystem.cs`
10. `mods/showcases/navigation/MassNavWebParityMod/UI/MassNavPlaygroundPanelController.cs`
