---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v0.1
适用范围: 高级逻辑 - 避障与Steering - Navigation2D 流场与RVO2
状态: 草案
依赖文档:
  - docs/03_基础服务/06_空间服务/00_总览.md
  - docs/03_基础服务/07_物理系统/00_总览.md
  - docs/04_游戏逻辑/10_导航寻路/00_总览.md
---

# Navigation2D 流场与RVO2 架构设计

# 1 设计概述
## 1.1 本文档定义
本文档定义 Ludots 在 Arch ECS 上实现 2D 群体引导与近场避障的“单一真源”口径：流场负责全局引导，RVO2/ORCA 负责近场互避，物理系统负责碰撞与积分。

## 1.2 设计目标
- 超高性能：按 SoA 数据通路组织，适配 Arch chunk 迭代与并行分片。
- 0GC：稳定状态每 tick 托管分配为 0，所有热路径 scratch 复用或 stackalloc。
- 可流式：以 chunkKey/tile 为粒度加载/卸载流场数据。
- 可裁决：更新频率、退化策略、预算点可配置且可审计。

## 1.3 设计思路
- 复用既有空间服务的“坐标口径与 streaming 口径”，但为避障邻居查询提供专用 0GC CellMap。
- 采用两级数据面：ECS 组件面作为权威状态，Navigation2DWorld 作为每 tick 的 SoA 缓存与 scratch。
- 输出口径统一为 ForceInput2D（cm/s^2），由 2D 物理积分系统消费并清零。

# 2 功能总览
## 2.1 术语表
| 术语 | 含义 |
|---|---|
| Surface | 流场所在的栅格空间（cellSizeCm + tileSizeCells） |
| Tile | 以 chunkKey 标识的稀疏栅格块，存储 obstacle/potential |
| Flow | 某个目标集对应的一张潜势场（potential）与采样逻辑 |
| CellMap | 以 cellKey 为键的邻居索引（cell->链表头 + next[]） |
| ORCA | RVO2 的半平面约束集合与线性规划求解 |

## 2.2 功能导图
- Pathing：不在本文档定义（由 `docs/04_游戏逻辑/10_导航寻路` SSOT 定义 corridor/path）。本文档只定义 flow+avoidance 的近场层。
- FlowField：Goal → Potential（增量迭代）→ DesiredVelocity 采样。
- Avoidance：CellMap 邻居枚举 → ORCA 约束 → 修正速度 → ForceInput2D。

## 2.3 架构图
```
ECS Components (SSOT)
  ├─ Position2D / Velocity2D
  ├─ NavGoal2D / NavFlowBinding2D / NavKinematics2D
  └─ ForceInput2D (output)

Navigation2DRuntime (per World singleton)
  ├─ Navigation2DWorld (SoA cache)
  ├─ CrowdSurface2D + CrowdFlow2D (tile store)
  └─ Nav2DCellMap + OrcaSolver2D
```

## 2.4 关联依赖
- 坐标与 streaming：`docs/03_基础服务/06_空间服务/00_总览.md`
- 物理积分与碰撞：`docs/03_基础服务/07_物理系统/00_总览.md`
- 全局寻路与 corridor：`docs/04_游戏逻辑/10_导航寻路/00_总览.md`

# 3 业务设计
## 3.1 业务用例与边界
用例：
- 大量单位共享目标或共享 flowId 目标，形成队列与绕障引导。
- 单位间互避，避免互穿与震荡。

边界：
- 不定义“如何求全局路径”，只消费 goal/corridor 的结果。
- 不定义业务语义（兵种、阵营），只通过 ID/组件表达。

## 3.2 业务主流程
1. 读取 NavFlowGoal2D（flowId=0）作为默认 flow 目标。
2. 预加载单位周边 tiles，并推进 flow 的增量迭代。
3. 收集 NavAgent2D 的 Position2D/Velocity2D/参数到 SoA。
4. 构建 CellMap 并为每个单位枚举邻居。
5. 采样 preferred velocity（优先 flow，否则直线朝向 NavGoal2D）。
6. ORCA 求解修正速度并输出 ForceInput2D。
7. 2D 物理在 IntegrationSystem2D 消费 ForceInput2D 积分到 Velocity2D 并清零。

## 3.3 关键场景与异常分支
- Tile 未加载：flow 采样返回失败，preferred velocity 退化为直线目标或 0。
- 预算不足：flow 迭代暂停在下一 tick 继续，避免超时；退化可观测（后续补齐指标）。
- MaxNeighbors 截断：只保留前 N 个邻居，防止 O(N^2) 膨胀。

# 4 数据模型
## 4.1 概念模型
- NavAgent2D：可被导航/避障系统消费的实体标记。
- NavKinematics2D：速度、加速度、半径、邻居半径、timeHorizon。
- NavGoal2D：点目标（未来可扩展区域/实体目标）。
- NavFlowBinding2D：加入哪个 Surface/Flow（当前实现默认 flowId=0）。
- NavFlowGoal2D：为某个 flowId 提供 goal（当前实现默认 flowId=0）。

## 4.2 数据结构与不变量
- Navigation2DWorld 为每 tick SoA 缓存：容量固定上限，超过上限直接截断。
- CrowdSurface2D/CrowdFlow2D 的 tileSizeCells 必须为 2 的幂。
- ForceInput2D 语义为 cm/s^2，必须在物理积分阶段清零。

## 4.3 生命周期/状态机
- Flow 状态：NeedsRebuild → Frontier 非空（迭代中）→ Frontier 为空（收敛）。
- Tile 生命周期：ChunkLoaded 创建 → ChunkUnloaded 释放（或由策略复用）。

# 5 落地方式
## 5.1 模块划分与职责
- FlowField：`src/Core/Navigation2D/FlowField/*`
- Avoidance：`src/Core/Navigation2D/Avoidance/*`
- Spatial：`src/Core/Navigation2D/Spatial/*`
- Runtime：`src/Core/Navigation2D/Runtime/*`
- Physics2D 集成系统：`src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`

## 5.2 关键接口与契约
- 配置入口：`GameConfig.Navigation2D`，默认配置见 `assets/Configs/Navigation2D/navigation2d.json`。
- Enable 开关：仅在 `Navigation2D.Enabled=true` 时创建 runtime 并尝试注册 steering 系统。
- 输出契约：导航系统只写 `ForceInput2D`，不直接写 Position2D。

## 5.3 运行时关键路径与预算点
- BuildSoA：按 chunk 顺序线性扫描，O(agentCount)。
- BuildCellMap：O(agentCount) 插入；查询每单位为 O(邻居候选)。
- ORCA：每单位 O(MaxNeighbors) 约束 + LP 求解。
- FlowStep：每 tick 受 `FlowIterationsPerTick` 限制推进。

# 6 与其他模块的职责切分
## 6.1 切分结论
- 空间服务：提供坐标域与通用查询，不承担避障热路径邻居构建。
- 导航寻路：提供远场 corridor/path，不承担近场互避。
- 物理系统：唯一负责速度积分与碰撞响应。

## 6.2 为什么如此
- 通用空间查询以稳定序与可读性优先，内部可使用 Dictionary/List；避障邻居查询必须 0GC 且极致性能，因此专用 CellMap。

## 6.3 影响范围
- 默认关闭 Navigation2D，不影响现有玩法与测试；启用后需保证相关组件装配与目标实体存在。

# 7 当前代码现状
## 7.1 现状入口
- FlowField：`src/Core/Navigation2D/FlowField/CrowdSurface2D.cs`、`src/Core/Navigation2D/FlowField/CrowdFlow2D.cs`
- ORCA：`src/Core/Navigation2D/Avoidance/OrcaSolver2D.cs`
- CellMap：`src/Core/Navigation2D/Spatial/Nav2DCellMap.cs`
- ForceInput2D 积分：`src/Core/Ludots.Physics2D/Systems/IntegrationSystem2D.cs`

## 7.2 差距清单
- flowId 多实例与多目标集尚未落地（当前默认 flowId=0）。
- Tile 与 AOI chunkKey 的映射仍需与空间服务 SSOT 对齐并补齐卸载策略。
- 缺少 dropped/预算统计的审计上报（后续应接入统一预算面板）。

## 7.3 迁移策略与风险
- 迁移路径：先让单位具备 NavAgent2D + NavKinematics2D + NavGoal2D + ForceInput2D，再逐步启用 flowId 与障碍栅格化。
- 风险：错误的 tileKey 映射会导致 flow 退化为直线或不收敛；必须通过测试用例覆盖。

# 8 验收条款
1. 0GC：稳定状态下 `Navigation2DAllocationTests` 验证每 tick 托管分配为 0，证据入口：`src/Tests/GasTests/Navigation2DAllocationTests.cs`。
2. 物理契约：ForceInput2D 必须在物理积分阶段被消费并清零，证据入口：`src/Core/Ludots.Physics2D/Systems/IntegrationSystem2D.cs`。
3. 可控启用：默认关闭，启用后必须可通过配置打开并可复现，证据入口：`assets/Configs/Navigation2D/navigation2d.json` 与 `src/Core/Config/GameConfig.cs`。
