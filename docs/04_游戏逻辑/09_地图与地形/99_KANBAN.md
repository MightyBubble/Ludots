---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 游戏逻辑 - 地图与地形 - 开发看板
状态: 进行中
---

# 地图与地形 KANBAN

# 1 定位
## 1.1 本看板的唯一真相范围

- 地图配置（MapConfig/模板装配）与地形数据（WorldMap/VertexMapBinary）的架构演进计划
- 地图加载流程（LoadMap/LoadVertexMap/MapLoaded）的约束落地计划
- 与地图资产/工具链相关的交付物（示例资产、校验工具、迁移工具）

## 1.2 不在本看板维护的内容

- 玩法系统需求与排期（战斗/AI/GAS 等）
- 平台渲染适配细节（Raylib/Unity/Godot 等）

# 2 使用规则
## 2.1 卡片字段规范

- 字段：ID / 标题 / Owner / 优先级 / 验收条件 / 证据入口
- 优先级：P0（阻塞/高风险）/ P1（重要）/ P2（优化）

## 2.2 状态流转规范

- Backlog → Doing → Review → Done
- 进入 Review 必须补齐验收条件与证据入口

## 2.3 WIP 上限与阻塞处理

- Doing WIP 上限：3
- 阻塞卡片必须写明阻塞原因与解除条件

# 3 看板
## 3.1 Backlog

- **MAP-001** 标题: 地图加载 fail-fast 与诊断信息收敛  
  Owner: X28技术团队 / 优先级: P0  
  验收条件: MapManager 不再吞掉 JSON 解析异常；LoadVertexMap 不再静默 return；错误包含 mapId/字段名/来源与候选路径证据  
  证据入口: `src/Core/Map/MapManager.cs`、`src/Core/Engine/GameEngine.cs`、`docs/04_游戏逻辑/09_地图与地形/05_对齐报告/01_地图架构对齐报告.md`

- **MAP-002** 标题: MapConfig schema 统一与一次性迁移  
  Owner: X28技术团队 / 优先级: P0  
  验收条件: 仓库内示例/Mod 地图配置均符合当前 schema；加载期对旧 schema 明确失败  
  证据入口: `src/Core/Config/MapConfig.cs`、`assets/Mods/**/assets/maps/*.json`

- **MAP-003** 标题: 路径约定收敛（Configs/assets/Data/Maps）  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: MapConfig 与 DataFile 的候选路径规则集中定义且有单测覆盖  
  证据入口: `src/Core/Map/MapManager.cs`、`src/Core/Engine/GameEngine.cs`

- **MAP-004** 标题: 明确地形真源与消费方边界（WorldMap vs VertexMap）  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 文档与代码均明确各系统依赖的真源；避免口径漂移  
  证据入口: `src/Core/Map/WorldMap.cs`、`src/Core/Map/Hex/VertexMap.cs`

- **MAP-005** 标题: 提供最小可加载地形二进制示例资产与校验用例  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 仓库内至少存在 1 个 VTXM 示例文件与对应 MapConfig，加载可稳定通过  
  证据入口: `src/Core/Map/Hex/VertexMapBinary.cs`、`assets/**`

- **MAP-006** 标题: 地图数据集口径（Hex/Grid/Graph）与叠加策略落地  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 每张最终地图可选择性加载 Hex/Grid/Graph；Hex(Vertex) 对该地图唯一；Grid/Graph 可多份叠加且禁止 silent override  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/02_接口规范/02_地图数据集与叠加口径.md`

- **MAP-009** 标题: MapLoaded 上下文契约补齐（Hex/Grid/Graph）  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: MapLoaded 上下文可明确判断 Hex(Vertex)/Grid/Graph 是否存在；ContextKeys 命名区分路网 Graph 与 GASGraph  
  证据入口: `src/Core/Scripting/ContextKeys.cs`、`src/Core/Engine/GameEngine.cs`、`docs/04_游戏逻辑/09_地图与地形/01_架构设计/08_地图数据初始化时序.md`

- **MAP-010** 标题: Vertex 唯一性裁决（最终地图维度）与冲突诊断  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 同一最终地图（Core+Mods+Parent 合并结果）出现多个不同 DataFile/Hex 数据源时加载期失败，并输出冲突来源证据（禁止 silent override）  
  证据入口: `src/Core/Map/MapManager.cs`、`src/Core/Engine/GameEngine.cs`、`docs/04_游戏逻辑/09_地图与地形/02_接口规范/02_地图数据集与叠加口径.md`

- **MAP-007** 标题: 路网 Graph 地图级加载接线（多份/多层/overlay）  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 地图加载后上下文可定位 GraphId；支持 MultiLayerGraph；支持静态 overlay + 运行期 overlay（GAS sink）叠加  
  证据入口: `src/Core/Navigation/*`

- **MAP-008** 标题: Grid coarse/fine 运行时容器与最小加载入口  
  Owner: X28技术团队 / 优先级: P2  
  验收条件: 地图加载后上下文可定位 coarse/fine；支持多份 GridId 并存；LMAP 占位逻辑有明确去留（实现或移除）；缺资产/不兼容可定位失败  
  证据入口: `src/Core/Map/WorldMap.cs`、`src/Core/Systems/MapLoader.cs`

- **MAP-TOOL-001** 标题: Hex 地形编辑器文档与后端接口对齐  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 明确编辑器读写接口、产物格式与 MapConfig 引用方式，并与现有 Web 编辑器能力对齐  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/07_使用手册/01_Hex地图编辑器.md`

- **MAP-TOOL-002** 标题: Grid 数据工具（后端接口 + 前端用户故事）  
  Owner: X28技术团队 / 优先级: P2  
  验收条件: coarse/fine 数据制作、校验与导出流程清晰；接口能定位 GridId/字段/来源  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/07_使用手册/02_Grid数据工具.md`

- **MAP-TOOL-003** 标题: Graph 路网工具（后端接口 + 前端用户故事）  
  Owner: X28技术团队 / 优先级: P2  
  验收条件: 分块/层级/overlay 的制作与校验流程清晰；接口能定位 GraphId/chunkKey/来源  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/07_使用手册/03_Graph路网工具.md`

- **MAP-TOOL-004** 标题: 实体装配工具（模板选择 + Overrides 表单 + 校验）  
  Owner: X28技术团队 / 优先级: P2  
  验收条件: 能编辑 Entities 与 Overrides；校验能定位 mapId/entityIndex/templateId/componentName  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/07_使用手册/04_实体装配工具.md`

## 3.2 Doing

- （空）

## 3.3 Review

- （空）

## 3.4 Done

- **MAP-DOC-001** 标题: 补齐 Hex/Grid/Graph/实体装配文档与多数据集对齐报告  
  Owner: X28技术团队 / 优先级: P1  
  验收条件: 总览索引更新；新增 Grid/Graph/实体装配/初始化时序文档；新增多数据集加载对齐报告；新增工具使用手册  
  证据入口: `docs/04_游戏逻辑/09_地图与地形/00_总览.md`、`docs/04_游戏逻辑/09_地图与地形/01_架构设计/*`、`docs/04_游戏逻辑/09_地图与地形/05_对齐报告/*`

# 4 里程碑
## 4.1 当前里程碑

- 完成地图加载 fail-fast 与 schema 统一（MAP-001/002）
- 明确“每张最终地图可选 Hex/Grid/Graph”口径并准备接线（MAP-006/007/008）
- 明确 MapLoaded 上下文契约与 Vertex 唯一性裁决（MAP-009/010）

## 4.2 下一个里程碑

- 收敛路径约定并给出示例资产与校验工具（MAP-003/005）

# 5 变更记录

- 2026-02-05：初始化地图与地形子系统看板。
- 2026-02-05：对齐 Hex/Grid/Graph/实体装配现状，补齐多数据集口径与工具手册，并扩展接线行动项。
