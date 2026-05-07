# 实体仿真阶段验收

本文定义“大规模实体仿真”工作的阶段性 UAT。验收目标不是只看单点功能，而是确保每一阶段都能给团队提供明确、可观察、可复现的结果。

## 1 验收原则

- 每个阶段都必须有独立入口
- 每个阶段都必须有用户可观察结果
- 每个阶段都要记录性能与语义
- 没有通过本阶段 UAT，不进入下一阶段主集成

## 2 UAT-0：contract 与单写真相

### 2.1 验收目标

确认正式组件 spec、车道分工、AOI 服务口径和位置单写规则已稳定。

### 2.2 验收项

- 任何一个 entity 都能回答：
  - 是否 authority
  - 是否 budgeted
  - 属于哪条 avoidance lane
  - 由哪条热路径写位置
- `WorldPositionCm`、`Position2D`、crowd SoA、`VisualTransform` 的职责边界有正式文档
- AOI 主语义是 `ILoadedChunks`，不是 `HexGridAOI` 专名

### 2.3 通过标准

- 文档、代码、任务拆分三者口径一致
- 无“同一个 entity 双写位置”的未决灰区

## 3 UAT-1：FormationPhysics 车道

### 3.1 验收目标

确认少量高价值方阵本体已走 `FullPhysics2D + OBB + ORCA/Hybrid` 正式车道。

### 3.2 操作步骤

1. 进入方阵本体验收入口
2. 生成 100、300、1000 规模的方阵本体样本
3. 下达相向移动、交叉移动、狭窄通过命令
4. 观察碰撞、推挤、转向、停下与恢复

### 3.3 期望结果

- 方阵本体之间不会直接穿透
- OBB / Box 碰撞能明显生效
- ORCA 或 Hybrid 对少量高价值单位有稳定效果
- `Physics2D -> WorldPositionCm` 同步后，视觉与逻辑位置一致

### 3.4 必记指标

- 100、300、1000 三档的：
  - `fps`
  - `frame ms`
  - 物理步数
  - 接触对数量
  - 每帧物理解算时间

## 4 UAT-2：MassCrowd 车道

### 4.1 验收目标

确认大量 crowd 已走正式 SoA / CrowdFlow 车道，并能支持方阵成员跟随与局部碰撞。

### 4.2 操作步骤

1. 进入 crowd 验收入口
2. 生成 2k、5k、10k 规模的 crowd
3. 分别测试：
   - 独立散布 crowd
   - 方阵跟随成员
   - 路人 / 羊群等预算实体
4. 观察局部碰撞、绕行、slot 跟随和密集区域表现

### 4.3 期望结果

- crowd 不依赖 `FullPhysics2D` 也能稳定运行
- 跟随成员不会把后端方阵真相拖乱
- 成员之间存在可感知的分离或避让
- 可见规模提升时仍保持可玩

### 4.4 必记指标

- 2k、5k、10k 三档的：
  - `fps`
  - `frame ms`
  - crowd step 耗时
  - prepare / steer / resolve / sync 耗时
  - 可见 crowd 数量

## 5 UAT-3：AOI / LOD / 预算

### 5.1 验收目标

确认 budgeted entity 会随 AOI 与 LOD 进入降频、休眠或去物质化，而 authority entity 不会被误裁。

### 5.2 操作步骤

1. 进入大地图预算入口
2. 放置：
   - authority 方阵
   - crowd 跟随成员
   - 路人
   - 羊群
3. 控制镜头与 focus 在不同区域来回切换
4. 观察 entity 的 LOD、tick、可见与物质化状态变化

### 5.3 期望结果

- authority entity 不会因镜头离开而失去后端真相
- budgeted entity 会随距离或 interest 变化进入降频
- AOI 服务不要求 hex board 才能工作
- 预算收益能在面板或日志中直接看到

### 5.4 必记指标

- 近景、中景、远景三档的：
  - materialized 数量
  - dormant 数量
  - dematerialized 数量
  - simulation budget 消耗
  - 可见实体数量

## 6 UAT-4：统一 showcase

### 6.1 验收目标

确认团队有一个统一入口能同时观察后端方阵真相、前端 crowd 表演和 AOI / LOD 预算。

### 6.2 操作步骤

1. 进入统一 showcase
2. 下达多组方阵命令
3. 观察方阵本体碰撞
4. 观察方阵内成员跟随和局部碰撞
5. 拉远镜头并切换区域
6. 观察成员被裁剪或降频、方阵本体保持真相

### 6.3 期望结果

- 后端方阵碰撞和前端 crowd 表演同时成立
- 远距离只裁剪或降频 budgeted entity
- performer 只负责表现，不承载逻辑真相
- 面板能展示：
  - lane 分布
  - LOD 分布
  - budget 消耗
  - 关键性能指标

### 6.4 必记指标

- 统一 showcase 下的：
  - authority 数量
  - budgeted 数量
  - visible / dormant / dematerialized 数量
  - physics 车道耗时
  - crowd 车道耗时
  - 总 `fps`
  - 总 `frame ms`

## 7 不通过条件

出现以下任一情况，本阶段 UAT 视为不通过：

- authority entity 会因镜头离开而失去逻辑真相
- 同一个 entity 同时被两条热路径写位置
- 为了“统一”而把万级 crowd 强行塞进 `FullPhysics2D`
- 只剩碎功能，没有独立可演示入口
- AOI 接口只能在 hex board 下工作

## 8 UAT 输出要求

每个阶段都必须至少输出：

- 验收入口名
- 测试步骤
- 通过 / 不通过结论
- 性能记录
- 已知限制
- 下一阶段依赖是否已满足
