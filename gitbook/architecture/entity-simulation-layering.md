# 实体仿真分层与车道

本文定义 Ludots 下一阶段“大规模实体仿真”工作的正式架构口径。目标不是引入新的大对象概念，而是在现有 ECS 约束下，用一组正式组件规范把以下需求收敛到同一套主线：

- 不可裁剪的后端真相实体
- 可裁剪、可降频、仍有行为逻辑的预算实体
- 少量高价值 authority entity 与大量 crowd 的分层避障
- 可切换 board 语义的 AOI / LOD
- presenter 与 gameplay entity 的职责分离

## 1 当前代码基线

当前仓库里已经存在以下正式挂靠点：

- 逻辑位置真相：`WorldPositionCm`、`PreviousWorldPositionCm`
- 物理热路径位置：`Position2D`、`PreviousPosition2D`
- 视觉可见性与视觉 LOD：`CullState`、`CameraCullingSystem`
- 层级过滤表达：`EntityLayer`、`LayerMask`
- Navigation / Physics 模式表达：`NavPhysicsMode`、`NavSolverMode`
- ORCA / Sonar steering kernels：`Ludots.Core.Navigation.Avoidance`
- OBB / Box 物理碰撞：`CollisionAlgorithms2D`
- AOI 服务接口：`ILoadedChunks`
- Board 级空间服务切换：`ApplyBoardSpatialConfig`

这说明当前缺口主要不是“没有零件”，而是：

- 组件口径还没有收敛成一套正式 spec
- 避障和碰撞还没有按 entity 车道分流
- AOI 公开语义仍带有 `HexGridAOI` 偏向
- 单写真相规则还没有形成正式文档

## 2 核心原则

- 不新增平行实体体系，一切通过组件组合表达
- 不让视觉裁剪状态直接充当逻辑裁剪真相
- 不让同一个 entity 同时被两条热路径双写位置
- 少量高价值实体与大量 crowd 可以按明确车道求解，但未落地的车道不得写成现有能力
- AOI 服务依赖 `ILoadedChunks`，不把引擎主语义绑定到单一 hex board

## 3 正式组件轴

### 3.1 真相与驻留

建议新增以下正式组件：

- `SimulationTickPolicy`
  - `FullRate`
  - `ReducedRate`
  - `Dormant`

解释如下：

- 真相与驻留的占位组件已删除（PR #730），正式驻留词汇由后续 workstream 收敛，不预造名字

### 3.2 移动参与与位姿写权

已落地以下正式组件（issue #643 阶段 0+1）：

- `MovementParticipation`（authoring，两轴参与声明）
  - `PhysicsPresence`：`None` / `Kinematic` / `Dynamic`，声明物理如何感知该 entity
  - `Displacement`：`Allowed` / `HandbackSpeedThresholdCmPerSec` / `MaxDurationMs`，声明 GAS 位移窗口策略
- `PoseAuthority`（runtime，位姿单写真相）
  - `Nav` / `Displacement` / `Physics`，初始值由 `PhysicsPresence` 推导：`None`/`Kinematic` → `Nav`，`Dynamic` → `Physics`

解释如下：

- 同一固定步内 `WorldPositionCm` 只允许当前 `PoseAuthority` 持有者写入；写权切换只在固定步边界经 `PoseAuthorityArbiter` + CommandBuffer 结算
- 没有 `MovementParticipation` 的实体不挂 `PoseAuthority`，行为与存量路径完全一致
- `EntityLayer + LayerMask` 继续作为统一层级过滤真相，不再新造一套 collision matrix 类型

### 3.3 Showcase-owned Formation 集群转发

Formation 是 Mod 业务聚合，不是 Core 仿真车道。`FormationCapabilityShowcaseMod` 拥有 anchor/member/slot 状态和初始布局；MassNavigation 只看到最终 member actor 与 typed MovePlan command group。

解释如下：

- Formation anchor 是 selectable、health、outline 等业务/表现锚点，不是 navigation actor，也没有 `OrderBuffer`；
- Formation member 才是普通 MassNavigation agent 和 order actor；
- Command Router 在 `CommandIntentProfile -> CastDispatch` 后调用 showcase-owned `FormationCommandActorExpander`；
- expander 按稳定 slot 顺序把 anchor 展开为 members，随后通过 clustered atomic batch 提交通用 `massNavigationMove`；
- GAS 把 member active order 投影为 `MovePlanExecutionIntent(CommandGroup)`，MassNavigation 返回 typed result，GAS 完成或取消 order；
- 不存在 Core Formation、Formation 专用 order、Q/E 旋转 consumer 或逐成员私有执行管线。

### 3.4 AOI 与仿真 LOD

建议新增以下正式组件：

- `InterestPolicy`
  - 描述该 entity 受哪些 interest source 影响
- `SimulationLodPolicy`
  - 距离档位、预算档位、切换阈值
- `SimulationLodState`
  - 当前仿真档位
- `MaterializationState`
  - `Materialized`
  - `Dormant`
  - `Dematerialized`

解释如下：

- `CullState` 只负责视觉
- `CullState.IsVisible` 是 camera viewport / spatial / loaded chunk gate 的可见性结果
- `CullState.LOD` 只表示视觉质量层级，不得作为剔除真相；距离 LOD 阈值不能形成相机跟随的圆形 visibility mask
- `CameraCullingSystem` 的距离阈值来自全局 `GameConfig.Presentation.CameraCulling`，即 `assets/Configs/game.json` 与所选 `<Mod>/assets/game.json` 经 `ConfigPipeline.MergeGameConfig()` 合并后的单例配置
- `SimulationLodState` 才是逻辑预算真相
- 允许同一个 entity 视觉可见但仿真降频
- 也允许同一个 entity 视觉不可见但仍保持后端真相更新

### 3.5 Core Minimap

- Core minimap 属于 Presentation 基建，不属于业务 Mod
- 正式逻辑信号源是 presenter authoring 中显式声明的 `MinimapMarker` behavior
- marker 位置唯一来自 presenter world position；颜色、尺寸、可见性和朝向来自 `MinimapMarker` behavior 配置/参数绑定
- `Name`、`MapEntity`、`Team` 都不得作为 marker 存在性的推断入口
- Visual heightmap、chunk streaming、camera culling、visual LOD 都不能 gate minimap 逻辑信号
- `IVisualHeightmapRenderSource` / `WorldSizeSpec` 只用于 RTS full-map preset 解析地图 bounds
- 256x256 大世界展示 authored presenter marker；不做名称推断、战略热力图或缺信号 fallback

## 4 仿真车道口径

### 4.1 精确物理车道候选（#643，尚未承诺）

历史 `FormationPhysics` 已从运行时代码删除，也没有可作为正式验收入口的 `FormationPhysicsPlaygroundMod`。[#643](https://github.com/MightyBubble/Ludots/issues/643) 只继续治理中性的精确物理车道是否值得新增：

- 若有真实业务消费者，再定义面向少量高价值 authority entity 的精确物理车道，并补齐调度和 UAT；
- 不恢复 Formation 专用 lane 名称，也不把 Formation Showcase 当作精确物理能力证据。

在 #643 完成前，下列内容只能作为候选设计，不能作为 Core 已交付合同：

- `NavPhysicsMode = FullPhysics2D`
- `NavSolverMode = PreciseOrca` 或 `Hybrid`
- 使用 `Collider2D.Box` 或其他正式 OBB 表达
- 位置由 `Physics2D -> WorldPositionCm` 单向同步

目标：

- 少量高价值 authority entity 做完整碰撞
- 少量权威单位走 ORCA / Hybrid
- 允许更高成本的近距离精确避障

### 4.2 MassNavigation 车道

适用对象：

- 数量多
- 可预算
- 可降频
- 手感与表演优先于完整物理精度

正式口径（已落地词汇）：

- `MovementParticipation.physicsPresence = none | kinematic`
- `PoseAuthority = Nav`（求解器写位姿；物理经 kinematic 存在看见 crowd）
- 不把全量 crowd 强塞进完整动态刚体模拟
- 热路径优先用 SoA crowd sim
- 上层业务可以持续提交明确逐成员目标，但 MassNavigation 不理解 formation、slot 或 follower 语义

目标：

- 屏幕内万级 crowd 仍可运行
- crowd 成员之间能彼此碰撞或分离
- 远距离能进入降频或 dormancy

## 5 单写真相规则

大规模仿真必须遵守以下正式单写规则：

- `WorldPositionCm` 是对外逻辑位置真相
- `VisualTransform` 只从 `WorldPositionCm` 插值，不反写
- `Position2D` 只服务 `FullPhysics2D` 热路径
- crowd SoA 位置只服务 `MassNavigation` 热路径
- 同一个 entity 不能同时被 `FullPhysics2D` 与 `MassNavigation` 双写
- `ForceInput2D` 和 MassNavigationFlow desired movement state 是派生输出，不是位置真相

允许存在的重复数据只有两类：

- 为热路径缓存服务的重复
- 为插值与表现服务的重复

禁止存在的重复真相只有一类：

- 没有明确 owner 的双向可写位置状态

## 6 AOI 服务语义

AOI 的正式服务语义如下：

- 引擎级主依赖是 `ILoadedChunks`
- `HexGridAOI` 只是 hex board 的一种实现
- 非 hex board 必须能够通过 `board.LoadedChunks` 暴露自己的 interest 集
- 引擎不再把 `HexGridAOI` 视为唯一 AOI 主语义

这意味着：

- AOI 可以来自 hex board
- AOI 可以来自 square grid board
- AOI 可以来自 streaming zone board
- 只要实现 `ILoadedChunks`，就能接入同一套调度语义

## 7 非目标

以下内容不在本阶段正式目标内：

- 一次性把所有 crowd 逻辑收敛进同一条通用 Physics2D 主线
- 为了追求统一而移除所有 SoA 热路径缓存
- 把 presenter 升格为逻辑 entity
- 把视觉裁剪逻辑直接当作仿真裁剪真相

## 8 现有挂靠点

后续实现应优先挂靠这些现有正式入口：

- `WorldPositionCm` / `PreviousWorldPositionCm`
- `EntityLayer` / `LayerMask`
- `NavActor` / `NavPhysicsMode` / `NavSolverMode`
- `Physics2DToWorldPositionSyncSystem`
- `MassNavigationSimulationRuntime`
- `CullState` / `CameraCullingSystem`
- `ILoadedChunks`
- `BoardRef`

如需扩展，先补正式基建，不在 feature 中临时绕路。
