# 实体仿真分层与车道

本文定义 Ludots 下一阶段“大规模实体仿真”工作的正式架构口径。目标不是引入新的大对象概念，而是在现有 ECS 约束下，用一组正式组件规范把以下需求收敛到同一套主线：

- 不可裁剪的后端真相实体
- 可裁剪、可降频、仍有行为逻辑的预算实体
- 少量高价值方阵与大量 crowd 的双车道避障
- 可切换 board 语义的 AOI / LOD
- performer 与 gameplay entity 的职责分离

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
- 少量高价值实体与大量 crowd 必须分车道求解
- AOI 服务依赖 `ILoadedChunks`，不把引擎主语义绑定到单一 hex board

## 3 正式组件轴

### 3.1 真相与驻留

建议新增以下正式组件：

- `SimulationAuthority`
  - 表示该 entity 承载战斗、指令、编队或其他后端真相
- `SimulationResidencyPolicy`
  - `AlwaysResident`
  - `BudgetedResident`
  - `Streamable`
- `SimulationTickPolicy`
  - `FullRate`
  - `ReducedRate`
  - `Dormant`

解释如下：

- 方阵本体、英雄、后端战斗单元挂 `SimulationAuthority + AlwaysResident`
- 路人、羊群、方阵内演员、非关键 crowd 挂 `BudgetedResident`
- `Streamable` 只用于极大世界中的远距离可重建实体，不用于后端真相

### 3.2 碰撞与避障参与

建议新增以下正式组件：

- `CollisionParticipation`
  - `None`
  - `CrowdOnly`
  - `Physics2D`
  - `Physics2DAndCrowd`
- `AvoidanceLane`
  - `FormationPhysics`
  - `MassNavigation`

解释如下：

- `CollisionParticipation` 决定 entity 参加哪种碰撞配对与求解
- `AvoidanceLane` 决定 entity 被哪条仿真热路径消费
- `EntityLayer + LayerMask` 继续作为统一层级过滤真相，不再新造一套 collision matrix 类型

### 3.3 编队本体与跟随成员

建议新增以下正式组件：

- `FormationAnchor`
  - 当前 entity 是后端方阵本体
- `FormationFootprint`
  - 方阵级碰撞体、宽度、深度、间距、朝向
- `FormationCommandState`
  - 阵型命令、目标点、旋转、速度策略
- `FormationFollower`
  - `AnchorEntity`
  - `SlotIndex`
  - `LocalOffset`
- `FollowerLocomotion`
  - 跟随阻尼、插值、局部扰动、slot 重分配参数

解释如下：

- 方阵本体承载真相
- 跟随成员只是普通 entity 的一种组件组合
- 跟随成员仍可拥有行为逻辑、碰撞、预算、LOD，不等于 performer

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
- 正式逻辑信号源是 performer authoring 中显式声明的 `MinimapMarker` behavior
- marker 位置唯一来自 performer world position；颜色、尺寸、可见性和朝向来自 `MinimapMarker` behavior 配置/参数绑定
- `Name`、`MapEntity`、`Team` 都不得作为 marker 存在性的推断入口
- Visual heightmap、chunk streaming、camera culling、visual LOD 都不能 gate minimap 逻辑信号
- `IVisualHeightmapRenderSource` / `WorldSizeSpec` 只用于 RTS full-map preset 解析地图 bounds
- 256x256 大世界展示 authored performer marker；不做名称推断、战略热力图或缺信号 fallback

## 4 双车道仿真口径

### 4.1 FormationPhysics 车道

适用对象：

- 数量少
- 高价值
- 承载战斗与阵型真相

正式口径：

- `AvoidanceLane = FormationPhysics`
- `NavPhysicsMode = FullPhysics2D`
- `NavSolverMode = PreciseOrca` 或 `Hybrid`
- 使用 `Collider2D.Box` 或其他正式 OBB 表达
- 位置由 `Physics2D -> WorldPositionCm` 单向同步

目标：

- 方阵本体之间做完整碰撞
- 少量权威单位走 ORCA / Hybrid
- 允许更高成本的近距离精确避障

### 4.2 MassNavigation 车道

适用对象：

- 数量多
- 可预算
- 可降频
- 手感与表演优先于完整物理精度

正式口径：

- `AvoidanceLane = MassNavigation`
- `NavPhysicsMode = NavCrowdResolve`
- `NavSolverMode = CrowdFlow`
- 不把全量 crowd 强塞进 `FullPhysics2D`
- 热路径优先用 SoA crowd sim

目标：

- 屏幕内万级 crowd 仍可运行
- 跟随成员之间能彼此碰撞或分离
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
- 把 performer 升格为逻辑 entity
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
