# Mass Nav Playground Façade

## 目标

为 `20k` 级 RTS 导航 playground 提供一条正式的高性能接入层：

- 保留 Ludots 的 `selection`、`UI runtime`、`presentation` 正式基建
- 不把 generic `selection/order` 便利层塞进高频热循环
- 在 `selection/order -> nav execution` 之间增加专用 `mass-nav façade`

## 第一性原理

`20k` agent 可玩性问题，本质上不是“有没有 ECS”，而是“控制面和数据面有没有分层”。

- 控制面负责：
  - 选中谁
  - 下达什么命令
  - 哪个编队模式生效
- 数据面负责：
  - 群体运行时
  - formation/group 解算
  - spatial hash
  - crowd update
  - 只在 dirty 时写回 `NavGoal2D`
- 表现面负责：
  - HUD
  - 面板
  - 选中高亮
  - 轻量渲染代理

## 正式边界

沿用现有 SSOT：

- selection 真相继续在 `SelectionRuntime`
- 输入继续在 `InputCollection`
- nav execution 继续落到 `NavGoal2D`
- HUD / panel / debug 继续走 `presentation`

新增 façade：

- `MassNavSimulationRuntime`
- `MassNavSelectionSyncSystem`
- `MassNavCommandBridgeSystem`
- 后续补齐：
  - `MassNavFormationRuntimeSystem`
  - `MassNavSpatialHashSystem`
  - `MassNavCrowdStepSystem`
  - `MassNavPresentationSystem`
  - `MassNavPanelPresentationSystem`

## 关键约束

- steady-state 禁止每帧 `SnapshotCurrentSelection`
- steady-state 禁止 generic order per-agent fan-out
- steady-state 禁止持续结构改动
- presentation 不直接读 selection 真相
- 编队状态属于 group runtime，不属于 per-agent 每帧组件改写

## MVP 切片

第一批实现先做四件事：

1. 用 revision-aware selection cache 取代旧的热路径 selection snapshot
2. 用新的 command bridge 直接把一次右键转换为一次 group move 应用
3. 用右上 HUD 打印关键调试指标
4. 为后续新 mod / 新 map 入口预留 runtime 骨架

## 后续开发图

- `必须改 core`
  - 增加 mass-command handoff，避免 generic `moveTo` 对大群体 fan-out
  - 补 caller-owned selection hot-path read API
- `应该改 playground`
  - 命令、过滤、渲染、HUD 不再每帧拍 selection 快照
  - 渲染读取 façade cache，而不是 selection contains
- `可保留`
  - `SelectionRuntime`
  - `OrderQueue / OrderBuffer`
  - `NavGoal2D -> steering` 边界
  - `UI runtime / presentation` 正式链路
