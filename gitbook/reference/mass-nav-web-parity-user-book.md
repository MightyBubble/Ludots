# MassNav Web Parity 用户教学书

本文从零开始介绍 `MassNavWebParityMod`。读者不需要知道 Ludots 内部历史，只要知道这是一个用 Ludots 正式链路演示“大世界万人导航、框选、右键移动、避障和表现同步”的 showcase。

## 你会得到什么

MassNav Web Parity 展示的是一张 RTS 风格的大地图：

- 地图/board 决定世界边界。
- 配置决定队伍、单位模板、障碍、热点、表现资源和导航调参。
- 玩家框选单位后右键下达移动命令。
- 单位以高性能 SoA solver 移动、避让、写回 ECS 位置。
- 视觉由 performer 管线渲染，选中 marker、小地图 marker、生命条都不是临时绘制。

当前它是“验收 showcase”，不是已经抽成通用产品的 `MassNavigation` SDK。你可以学习和改配置，但还不能只随便给任何 Mod 写一个模板就自动得到完整大世界万人导航。

## 五分钟跑起来

在仓库根目录执行：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity --adapter raylib --build auto
```

也可以使用 preset：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_nav_web_parity_raylib
```

启动后检查这些画面：

- 大量单位分布在地图上。
- 左侧有 MassNav 调参面板。
- 右上角有诊断 HUD。
- 小地图显示全图和视窗。
- 框选单位后，单位脚下出现 selection marker。
- 右键地面后，单位移动而不是重置回出生点。

## 核心概念

世界边界来自 map 的 board，不来自 MassNav 私有配置。当前地图在 `mods/showcases/navigation/MassNavWebParityMod/assets/Maps/mass_nav_web_parity.json` 中 author board；运行时通过 `PrimaryBoard.WorldSizeSpec.Bounds` 读取。

ECS 坐标是权威世界坐标。MassNav solver 使用本地 SoA cache 工作，然后把结果写回 `WorldPositionCm`、`PreviousWorldPositionCm` 和 `FacingDirection`。

Solver window 是当前高精度求解窗口。现在固定为 10,000 x 10,000 cm，因为底层 `MassNavWebParitySimState` 的网格、hash 和数组容量仍是固定 cache。`MassNavWebParityConfig.world.solverWindowWidthCm/HeightCm` 必须匹配这个 cache。

Flow work area 是当前关注区域，由镜头、最近命令目标、选中单位范围共同驱动。它可以比 solver window 大，用来描述下一步该优先处理的区域。

`flow.enabled=false` 不代表不能移动。当前 showcase 主要使用本地 steering、formation target 和 avoidance；flow field 管线是可选能力，在这份验收配置里默认关闭。

## 文件地图

主要入口在 `mods/showcases/navigation/MassNavWebParityMod/assets/`：

- `MassNavWebParityConfig.json`：队伍、agent profiles、障碍、热点、cadence、arrival、avoidance、crowd semantics、presentation id 引用。
- `Entities/templates.json`：agent、blocker、hotspot、local player 等实体模板。
- `GAS/order_types.json`：`massNavMove` 订单类型和它的规则。
- `Presentation/performers.json`：单位主体、生命条、选中 marker、小地图 marker、障碍、热点 performer。
- `Presentation/mesh_assets.json` 和 `Presentation/host_assets.json`：模型资源和后端文件绑定。
- `Maps/mass_nav_web_parity.json`：地图、board、局部玩家 entity、视觉地形。
- `Configs/Camera/virtual_cameras.json`：战术/战略镜头 profile。
- `game.json`：启动 map、presentation capacity、selection preview 订单列表、Navigation2D 开关等全局配置片段。

配置通过 `ConfigPipeline` 合并。不要在业务代码里新写私有 JSON 加载器。

## 配置里的数字和语义名

MassNav 配置里有两类值。第一类是真实数值，例如 `agentsPerTeam`、`simulationHz`、`radiusCm`、`speedCmPerSecond`、`sizePx`、颜色、scale、offset、queue size 和 timeout。它们就是调参值，应该继续写数字。

第二类是运行时内部 handle，例如 performer `slot`、`paramKey`、`speedParamKey`、`orderTypeId`、order blackboard key、validation graph id。用户配置不应该记这些数字。现在 MassNav authoring 使用语义字符串：

- performer 行为槽写 `"slot": "body"`、`"grounding"`、`"animator"`、`"minimap"`。
- performer 参数写 `"massNav.agent.health.ratio"`、`"massNav.agent.health.current"`、`"blacksmith.worker.locomotion.speed"`。
- 禁用可选参数写 `"none"`，加载后才编译成内部 `-1` 或 `0` 哨兵。
- `massNavMove` 不手写 `orderTypeId`；`OrderTypeConfigLoader` 按 key 稳定分配 int，并在运行时继续走 `OrderTypeRegistry`。
- order rule 引用写 `interruptsActiveOrderTypeKeys`，不要写其它 order 的数字 id。

这些语义名只存在于加载期。真正的仿真、performer tick、order buffer 和 bitset 仍然使用 int、数组和 bitmask，不把字符串带进热路径。

## 最小单位模板

一个可被 MassNav 控制的单位模板必须包含这些正式组件：

- `Team`
- `OrderBuffer`
- `SelectionSelectableTag`
- `SelectionSelectableState`
- `WorldPositionCm`
- `FacingDirection`
- `MassNavAgentTag`
- `MassNavControllable`

`WorldPositionCm` 会通过 component registry authoring 补齐 `PreviousWorldPositionCm`、`VisualTransform`、`CullState`。模板读者不需要手动把这三个全写出来，但 spawn receipt binding 会校验运行时实体最终具备它们。

单位 profile 不写在 C# switch 里。当前配置使用 `agentProfiles.profiles[]` 决定 light/heavy、`navMass`、`visualScale` 和每 N 个单位分配一次的规则。

## 表现和选中 Marker

单位主体走 performer。当前 agent 使用 Blacksmith showcase 的 `blacksmith.worker.knight` skinned mesh 和动画 profile；MassNav 不复制这套素材。

选中 marker 不是 agent 根 performer 里常驻隐藏 mesh。系统在选中时创建独立 scoped performer，取消选中时销毁它。marker scope 使用 owner 的 `PresentationStableId`，reset 时也走正式 presentation destroy pending 流程。

这意味着你检查 marker 时应关注：

- 选中后每个 live owner 只有一个 marker。
- light/heavy profile 切换时旧 marker definition 会被销毁。
- reset 后旧 owner 的 performer 不应继续留在新场景选择集合里。
- core performer 代码不包含 MassNav 专属分支。

## 命令链路

右键移动的链路是：

```text
Input
  -> SelectionRuntime
  -> OrderBuffer(massNavMove)
  -> MassNavOrderBridgeSystem
  -> MassNavGroupRuntime
  -> MassNavWebParitySimState
  -> ECS position/facing writeback
  -> performer transform sync
```

`massNavMove` 的 id 由 `GAS/order_types.json` 注册到 `OrderTypeRegistry`。selection move path preview 允许预览哪些订单，由 `selection.movePathPreviewOrderTypeKeys` 配置，不应在 CoreInput 源码里硬编码 showcase 订单名。

## 如何改成自己的场景

保守做法是先复制 MassNav showcase 的配置形状，然后逐项改：

1. 在 `MassNavWebParityConfig.json` 修改 `scenario.teams` 和 `agentsPerTeam`。
2. 在 `presentation.teams` 为每个 team 绑定 light/heavy template 和 performer。
3. 在 `agentProfiles.profiles` 调整 `navMass`、`visualScale`、heavy 分布规则。
4. 在 `world.obstacles` author 障碍，坐标是 solver window 内的本地 cm。
5. 在 map 的 board 中 author 世界尺寸和 visual heightmap。
6. 在 `Presentation/performers.json` 调整模型、颜色、生命条、小地图 marker 和 selection marker。

注意当前 solver window 仍是固定 cache。可以移动热点和大世界窗口，但不能把 solver cache 配成任意尺寸。

## 调参指南

`cadence` 控制各子系统频率：

- `simulationHz`：仿真步频。
- `targetUpdateHz`：formation/group target 刷新频率。
- `hardResolveHz`：硬穿透修正频率。
- `entitySyncHz`：写回 ECS 的频率。
- `maxStepsPerFixedTick`：单个 fixed tick 最多补几步。

`arrival` 控制到达和卡住恢复：

- `timeoutMs`：停滞多久触发恢复。
- `progressDistanceCm`：认为“有进展”的距离。
- `wakePushDistanceCm`：被推离后重新唤醒的距离。
- `maxRetryCount`：最多重试次数。

`avoidance` 控制不同质量单位之间的推挤策略：

- `dominantMassRatio`：质量差达到多少认为是 dominant push。
- `friendlyResponseScale`：友军协作避让强度。
- `nonFriendlyResponseScale`：非友军阻挡响应。
- `dominantPushResponseScale`：重单位推开轻单位的响应。

`semantics` 控制更细的 gameplay 语义，例如障碍硬半径、目标投影 clearance、阵型 slot 间距、速度、分离半径和速度平滑。

## 常见问题

看不到单位：

- 检查 `PerformerBlacksmithShowcaseMod` 是否在 `mod.json` dependencies 中。
- 检查 `Presentation/host_assets.json` 是否能解析模型源文件。
- 检查 map 是否绑定 visual heightmap。

框选没有 marker：

- 检查本地玩家模板是否 author `PlayerOwner` 和 `SelectionDragState`。
- 检查 agent 模板是否 author selection 组件。
- 检查 performer definition 是否存在 `massnav_agent_selection_marker_light/heavy`。

右键不移动：

- 检查 `massNavMove` 是否注册进 `OrderTypeRegistry`。
- 检查 `selection.movePathPreviewOrderTypeKeys` 只是预览配置，不等于命令提交配置。
- 检查 agent 是否有 `OrderBuffer` 和 `MassNavAgentIndex`。

reset 后表现异常：

- 旧 MassNav 实体应标记 `PresentationDestroyPending`，等待 presentation 生命周期发布 destroy 事件并最终销毁。
- 新场景 spawn 通过 `RuntimeEntitySpawnQueue` 和 receipt channel 绑定，不直接 `world.Create` 业务实体。

改了 solver window 尺寸后失败：

- 这是当前限制。配置必须匹配固定 10,000 x 10,000 cm SoA cache。通用可变尺寸 solver 需要后续提取 `MassFlow`/`MassNavigation` 后再做。

## 架构边界

当前 showcase 已经复用正式链路：`ConfigPipeline`、template spawn、`SelectionRuntime`、`OrderBuffer`、`OrderTypeRegistry`、performer、minimap 和 presentation lifecycle。

当前还不是通用配置产品的原因：

- solver cache/grid/hash 仍固定在 C# 常量上。
- spawn/formation layout 仍有 showcase 场景逻辑。
- strategic/tactical camera 行为还有 showcase-coded reset 逻辑。
- team relationship 会写入全局 `TeamManager`，需要后续正式分层。
- 通用“只配置 entity 模板就接入万人导航”的 contract 尚未抽到 `MassNavigation`。

因此，使用者应把它当作高性能大世界导航的学习和验收书，而不是最终公共 API。

## 下一步学习

- 正式链路手册：`gitbook/reference/mass-nav-web-parity-playground.md`
- Mod 边界说明：`mods/showcases/navigation/MassNavWebParityMod/README.md`
- 关键 runtime：`Runtime/MassNavSimulationRuntime.cs`
- 热路径 solver：`Runtime/MassNavWebParitySimState.cs`
- 选择 marker：`Systems/MassNavSelectionPerformerSyncSystem.cs`
- 命令桥：`Systems/MassNavOrderBridgeSystem.cs`
