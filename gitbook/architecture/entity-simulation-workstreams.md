# 实体仿真工作流拆分

本文把“大规模实体仿真”工作拆成可并行分派、又能阶段性看见结果的正式 workstream。目标是：

- 不搞一口吃完的大一统重构
- 不把团队拆成只做微小碎片、长期看不到结果
- 每个 workstream 都有清晰依赖、交付物和阶段可玩入口

## 1 拆分原则

- 先立 contract，再分车道，再做 AOI/LOD，最后做集成 showcase
- 每个 workstream 只解决一类正式缺口
- 每个 workstream 都必须有独立可演示结果
- 任何阶段都不得破坏现有主线的单写真相规则

## 2 推荐里程碑

建议按 4 个阶段推进：

- `M1`：正式 contract 收敛，可让不同 agent 在稳定边界内并行开发
- `M2`：方阵本体车道可跑，可看到少量高价值单位的 OBB + ORCA 结果
- `M3`：crowd 车道可跑，可看到大量跟随成员的局部碰撞和表演
- `M4`：AOI / LOD / 预算接入，可在大地图里看见预算收益

## 3 Workstream 0：正式 contract 与真相审计

### 3.1 目标

把组件 spec、单写规则、车道语义、AOI 服务口径收敛成正式 contract。

### 3.2 交付物

- 正式组件定义
- 单写真相约束
- `AvoidanceLane` / `CollisionParticipation` 正式枚举
- AOI 服务主语义只依赖 `ILoadedChunks`
- 现有重复真相清单与禁止项

### 3.3 依赖

- 无前置依赖

### 3.4 后续依赖方

- Workstream 1
- Workstream 2
- Workstream 3

### 3.5 完成标准

- 文档与代码口径一致
- 能明确回答每种 entity 由哪条热路径写位置
- 能明确回答 AOI 服务对 board 的最小契约
- 不能再出现“同一个 entity 同时归属于两条位置写链”的灰区

### 3.6 阶段成果

- 团队可以开始并行开发
- 不必等待大集成才知道边界是否正确

## 4 Workstream 1：FormationPhysics 车道

### 4.1 目标

为少量高价值实体建立正式的 `FullPhysics2D + OBB + ORCA/Hybrid` 车道。

### 4.2 范围

- 方阵本体
- 后端战斗载体
- 低数量高价值实体

### 4.3 交付物

- `AvoidanceLane = FormationPhysics` 运行时调度
- `NavSolverMode` 真正参与 steering 分流
- `Collider2D.Box` 与 OBB 碰撞在方阵本体上可跑
- `FullPhysics2D -> WorldPositionCm` 单向同步链稳定
- 方阵本体之间的碰撞、推挤、相对避让可见

### 4.4 依赖

- 依赖 Workstream 0 的 contract

### 4.5 对其他 workstream 的影响

- 为 Workstream 2 提供方阵 anchor 真相
- 为 Workstream 4 提供少量高价值单位的正式后端车道

### 4.6 完成标准

- 100 到 1000 级别的方阵本体可以稳定运行
- 不使用 crowd SoA 车道写同一批 entity
- 不要求大规模 crowd 性能最优

### 4.7 阶段成果

- 可以在一个独立 demo 中看到“后端方阵真相碰撞”
- 这是第一个对外可见的大结果，不是纯基建

## 5 Workstream 2：MassNavigation 车道

### 5.1 目标

为大量预算实体建立正式的 `CrowdFlow / SoA` 车道，并支持方阵跟随成员、路人、羊群等场景。

### 5.2 范围

- 方阵跟随成员
- 大量路人
- 羊群等装饰性行为体

### 5.3 交付物

- `AvoidanceLane = MassNavigation` 运行时调度
- crowd SoA 热路径与 ECS 同步链
- 跟随成员的 slot 跟随与局部碰撞
- crowd entity 的 `BudgetedResident` 运行态
- 不依赖 `FullPhysics2D` 承载全量 crowd

### 5.4 依赖

- 依赖 Workstream 0 的 contract
- 可以与 Workstream 1 并行

### 5.5 对其他 workstream 的影响

- 为 Workstream 3 提供可降频的真实对象
- 为 Workstream 4 提供可见的 crowd 表演结果

### 5.6 完成标准

- 单屏可展示大规模 crowd 跟随与局部碰撞
- 与方阵本体的后端真相分离
- 不把 performer 混入逻辑实体路径

### 5.7 阶段成果

- 可以单独演示“大量跟随成员的流动和碰撞”
- 这是第二个对外可见的大结果，不等待 AOI 才能看效果

## 6 Workstream 3：AOI / LOD / 预算调度

### 6.1 目标

让 `BudgetedResident` entity 真正进入可裁剪、可降频、可休眠的正式运行态，并把 AOI 服务从 hex 偏向收敛到 board-agnostic 语义。

### 6.2 交付物

- `SimulationLodPolicy` / `SimulationLodState`
- `MaterializationState`
- board-agnostic AOI 服务挂接
- 基于 interest source 的预算调度器
- crowd 的近中远档预算策略
- authority entity 永不裁剪的正式约束

### 6.3 依赖

- 依赖 Workstream 0
- 对 Workstream 1 仅弱依赖
- 对 Workstream 2 有强依赖，因为预算调度要真正消费 crowd 车道

### 6.4 完成标准

- 非 authority entity 可以进入 `ReducedRate` / `Dormant`
- authority entity 不会因 AOI 丢失后端真相
- AOI 主语义不再要求 hex board
- 大地图下可以观察到真实预算收益

### 6.5 阶段成果

- 这是第三个大结果
- 不是只优化一个小点，而是第一次在大世界里看见预算调度真正生效

## 7 Workstream 4：集成 showcase 与正式主线入口

### 7.1 目标

把前三条线收敛到一个统一可玩的 showcase / playground，形成团队共同对齐的验收入口。

### 7.2 交付物

- 方阵本体车道 + crowd 车道的统一接线
- 运行时调参与观测面板
- 统一的 demo map / launcher binding
- 集成 UAT 记录

### 7.3 依赖

- 依赖 Workstream 1
- 依赖 Workstream 2
- 依赖 Workstream 3

### 7.4 完成标准

- 可以在一个正式入口里看到：
  - 后端方阵碰撞
  - 前端成员跟随与局部碰撞
  - 镜头离开后成员进入降频或裁剪
  - 方阵本体保持后端真相
- 面板里能看到预算、LOD、可见数量、运行态变化

### 7.5 阶段成果

- 团队终于有一个“统一看结果”的入口
- 避免长期停留在多条半成品分叉线

## 8 并行分派建议

推荐分派方式如下：

- Agent A：Workstream 0
- Agent B：Workstream 1
- Agent C：Workstream 2
- Agent D：Workstream 3
- Agent E：Workstream 4 在前三条线接近完成后开始集成

推荐节奏如下：

- 第 1 周优先完成 Workstream 0
- Workstream 1 和 Workstream 2 在 contract 稳定后并行
- Workstream 3 在 contract 稳定后即可启动，但以 crowd 车道的真实运行态为主输入
- Workstream 4 不提前吞并所有工作，只做集成和验收入口

## 9 禁止的拆分方式

禁止以下几种拆法：

- 按“写几个组件、写几个 system”做完全碎片化切分
- 把 AOI、LOD、预算、物质化、裁剪拆成大量互相看不见结果的小功能
- 在 Workstream 1 或 Workstream 2 中偷偷改写对方的真相链
- 在没有 contract 的情况下并行修改位置同步规则

## 10 推荐交付顺序

推荐最终交付顺序如下：

1. Workstream 0 文档与 contract 落地
2. Workstream 1 可玩 demo
3. Workstream 2 可玩 demo
4. Workstream 3 大地图预算 demo
5. Workstream 4 统一 showcase

这样既不会一口吃成大胖子，也不会一直做碎功能看不到结果。
