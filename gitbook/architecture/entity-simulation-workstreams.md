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
- `M2`：#643 对精确物理车道作出删除或中性化决议；只有中性化后才进入实现
- `M3`：crowd 车道可跑，可看到大量实体的局部碰撞和表演
- `M4`：AOI / LOD / 预算接入，可在大地图里看见预算收益

## 3 Workstream 0：正式 contract 与真相审计

### 3.1 目标

把组件 spec、单写规则、车道语义、AOI 服务口径收敛成正式 contract。

### 3.2 交付物

- 正式组件定义
- 单写真相约束
- `MovementParticipation` / `PoseAuthority` 正式组件（两轴参与声明 + 位姿写权，issue #643 阶段 0+1 已落地）
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

## 4 Workstream 1：精确物理车道决议（#643）

### 4.1 目标

历史 `FormationPhysics` 枚举与解析入口已经删除。此 workstream 只评估是否存在真实业务需要，把少量高价值 authority entity 放入中性的 `FullPhysics2D + OBB + ORCA/Hybrid` 车道；它不是已交付运行时承诺。

### 4.2 范围

- 少量高价值 authority entity
- 后端战斗载体
- 真实业务消费者与可玩验收入口

### 4.3 交付物

- #643 明确记录删除或中性化决议
- 若删除：清理枚举、文档、测试和虚构 UAT 入口
- 若中性化：另立实现任务，补真实 lane 名称、`NavSolverMode` 分流、`Collider2D.Box` / OBB、`FullPhysics2D -> WorldPositionCm` 单向同步和可玩 UAT

### 4.4 依赖

- 依赖 Workstream 0 的 contract

### 4.5 对其他 workstream 的影响

- 不阻塞 Workstream 2 的通用 MassNavigation 明确目标执行
- 为 Workstream 4 提供少量高价值单位的正式后端车道

### 4.6 完成标准

- #643 已作出明确决议
- 文档不再把不存在的 lane 或 playground 写成现有能力
- 若选择中性化，实现与 UAT 由后续任务定义，本 workstream 不偷跑

### 4.7 阶段成果

- 当前阶段成果是消除虚假承诺
- 只有 #643 选择中性化并完成后续实现后，才可以声明独立精确物理 demo

## 5 Workstream 2：MassNavigation 车道

### 5.1 目标

为大量预算实体建立正式的 `CrowdFlow / SoA` 车道，并支持独立 crowd、路人、羊群，以及由上层业务提交明确成员目标的场景。

### 5.2 范围

- 接收明确空间目标的普通 MassNavigation agents
- 大量路人
- 羊群等装饰性行为体

### 5.3 交付物

- `AvoidanceLane = MassNavigation` 运行时调度
- crowd SoA 热路径与 ECS 同步链
- `massNavigationMove` 只接收明确空间目标
- `MovePlanExecutionIntent` / `MassNavigationMovePlanExecutionSink` 接收上层已解析的逐实体目标
- crowd entity 的 `BudgetedResident` 运行态
- 不依赖 `FullPhysics2D` 承载全量 crowd
- 不拥有 formation identity、slot layout、facing、rotation 或 follower runtime

### 5.4 依赖

- 依赖 Workstream 0 的 contract
- 可以与 Workstream 1 并行

### 5.5 对其他 workstream 的影响

- 为 Workstream 3 提供可降频的真实对象
- 为 Workstream 4 提供可见的 crowd 表演结果

### 5.6 完成标准

- 单屏可展示大规模 crowd 移动与局部碰撞
- 上层 Formation Showcase 可作为明确逐成员目标的一个消费者，但不能改变 Core 合同
- 不把 performer 混入逻辑实体路径

### 5.7 阶段成果

- 可以单独演示“大量 crowd 的流动和碰撞”
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

- 可选精确物理车道 + crowd 车道的统一接线；若 #643 删除前者，则不保留空壳接线
- 运行时调参与观测面板
- 统一的 demo map / launcher binding
- 集成 UAT 记录

### 7.3 依赖

- #643 若选择中性化并进入实现，则依赖对应的 Workstream 1 后续任务；若删除则无此依赖
- 依赖 Workstream 2
- 依赖 Workstream 3

### 7.4 完成标准

- 可以在一个正式入口里看到：
  - #643 若选择中性化：少量高价值单位的精确物理碰撞
  - crowd 移动与局部碰撞
  - Showcase-owned formation 通过明确成员目标接入，而不是进入 Core
  - 镜头离开后成员进入降频或裁剪
  - authority entity 保持后端真相
- 面板里能看到预算、LOD、可见数量、运行态变化

### 7.5 阶段成果

- 团队终于有一个“统一看结果”的入口
- 避免长期停留在多条半成品分叉线

## 8 并行分派建议

推荐分派方式如下：

- Agent A：Workstream 0
- Agent B：Workstream 1 的 #643 治理；不得在决议前实现虚构车道
- Agent C：Workstream 2
- Agent D：Workstream 3
- Agent E：Workstream 4 在前三条线接近完成后开始集成

推荐节奏如下：

- 第 1 周优先完成 Workstream 0
- Workstream 1 的治理和 Workstream 2 可并行；精确物理实现必须等待 #643 决议
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
2. Workstream 1 完成 #643 决议；只有中性化并另行实现后才加入可玩 demo
3. Workstream 2 可玩 demo
4. Workstream 3 大地图预算 demo
5. Workstream 4 统一 showcase

这样既不会一口吃成大胖子，也不会一直做碎功能看不到结果。
