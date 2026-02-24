---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 游戏逻辑 - 地图与地形 - Grid地图系统
状态: 草案
---

# Grid地图系统 架构设计

# 1 设计概述
## 1.1 本文档定义

本文档定义 Ludots 的 Grid 地图系统口径：用于“天气/流场等场数据”的大颗粒与小颗粒网格，以及其在地图加载后的上下文注入方式与叠加策略。

边界与非目标：

- 边界：只定义 Grid 数据结构、数据集/层的抽象与加载期契约，不定义具体天气模型、流体求解器或渲染表现。
- 非目标：不为历史格式保留静默兼容；缺数据/格式不匹配必须在加载期失败（目标口径）。

## 1.2 设计目标

- 多分辨率：同一张地图可同时拥有 coarse（大颗粒）与 fine（小颗粒）两套网格，服务不同系统（天气/流场）。
- 多份并存：同一张地图允许挂载多个 Grid 数据集（不同用途、不同分辨率、不同数据域）。
- 可叠加：允许多个来源（Core/Mods/Parent/Child）为同一 GridId 提供补丁或覆盖，但必须可解释且禁止 silent override。
- 可诊断：加载期能定位“哪个 GridId、哪个来源、哪个路径”导致失败。

## 1.3 设计思路

- 用“地图数据集（MapDataSet）”描述 Grid 的可选加载与叠加，不把 Grid 的扩展写死到 MapLoader。
- Grid 以“层/场（Field）”为中心建模：每个 GridId 对应一个空间采样网格，具体字段由上层系统解释（例如温度、湿度、风向、流速等）。

# 2 功能总览
## 2.1 术语表

- GridId：一个 Grid 数据集的稳定标识（例如 `grid_coarse_weather`、`grid_fine_flow`）。
- Coarse/Fine：同一地图上不同分辨率的网格集合，通常 coarse 用于宏观系统（天气），fine 用于局部系统（流场/气味等）。
- Field：网格上的数据域（标量/向量/离散枚举），例如温度（float）、风向（Vector2）、降雨等级（byte）。

## 2.2 功能导图

- Grid 数据集声明（MapConfig / MapDataSet）
- 加载期注入（MapLoaded 上下文）
- 运行期查询与更新（系统读取/写入 Field）
- 叠加策略（同 GridId 的多来源合并）

## 2.3 架构图

```
MapConfig(数据集清单)
  -> GridDataSetResolver(裁决/合并)
      -> GridRegistry(按 GridId 存储)
          -> GridLayer(coarse/fine, dims, cellSize, fields)
```

## 2.4 关联依赖

- 现有 grid 容器：`src/Core/Map/WorldMap.cs`、`src/Core/Map/MapTile.cs`
- 地图加载编排：`src/Core/Engine/GameEngine.cs`

# 3 业务设计
## 3.1 业务用例与边界

- 用例：地图加载后，天气系统拿到 coarse grid，流场系统拿到 fine grid；二者互不干扰，且可以通过 Mod 追加新的 grid 数据集。
- 边界：Grid 系统只提供空间采样数据容器与加载期契约，不定义“如何更新天气/流场”的业务规则。

## 3.2 业务主流程

```
LoadMap(mapId)
  -> ResolveMapDataSets(MapConfig) 得到 Grid 数据集清单（可为空，可多份）
  -> LoadGrids(...) 加载/创建 GridRegistry
  -> ctx.Set(GridRegistry) 在 MapLoaded 上下文注入
```

## 3.3 关键场景与异常分支

- Grid 资产缺失：声明了 Grid 数据集但找不到文件，加载期失败并输出候选路径与来源。
- schema 不匹配：字段类型/维度/单位不符合约定，加载期失败并定位 GridId。
- 多来源冲突：同一 GridId 被多个来源以不兼容方式覆盖，必须按合并策略裁决；若无策略或无法合并，则失败。

# 4 数据模型
## 4.1 概念模型

- GridRegistry：地图加载后的运行时容器，按 GridId 管理多个 GridLayer。
- GridLayer：一个网格实例，包含：
  - 空间定义：dims、cellSize、origin/offset（单位口径必须与引擎“厘米域”对齐）
  - 字段集合：FieldId → 数据数组（标量/向量/离散）

## 4.2 数据结构与不变量

- 不变量：同一 GridId 在同一张最终地图中必须解析为“一个确定的 GridLayer”（允许来自多来源合并，但结果唯一）。
- 不变量：所有空间单位必须明确（推荐以厘米域为真源；必要时由 Adapter 转换）。

## 4.3 生命周期/状态机

- 未加载：GridRegistry 不存在或为空。
- 已加载：MapLoaded 注入后，系统可读写 Field。
- 切图：加载新 MapId 后，应替换为新地图对应的 GridRegistry（按 map 维度隔离）。

# 5 落地方式
## 5.1 模块划分与职责

- MapConfig/MapDataSet：声明 Grid 数据集与叠加策略（目标形态）。
- GridRegistry：地图加载后的运行时容器（目标形态）。
- 业务系统（天气/流场）：消费 GridRegistry 并维护 Field。

## 5.2 关键接口与契约

- MapLoaded 上下文必须能提供 GridRegistry（或显式表明该地图不提供任何 grid）。
- 同一 GridId 的合并策略必须显式（Replace/Merge/Additive）；无策略不得 silent override。

## 5.3 运行时关键路径与预算点

- coarse/fine grid 维度差异巨大时，内存与更新频率必须分级；避免把 fine grid 当做每帧全量更新的黑洞。

# 6 与其他模块的职责切分
## 6.1 切分结论

- Hex(VertexMap) 解决“地表/水体/阻挡”等地形真源；Grid 解决“天气/流场等场数据”，两者通过统一坐标/单位口径协作，但不互为真源。

## 6.2 为什么如此

把地形真源与场数据解耦能避免“同一数据被两套系统重复表达”，并允许独立演进（例如天气完全不依赖 VertexMap）。

## 6.3 影响范围

- 上层系统必须明确依赖：需要地形就读 VertexMap，需要场数据就读 GridRegistry，禁止用 WorldMap 的默认值去隐式补齐。

# 7 当前代码现状
## 7.1 现状入口

- WorldMap 容器：`src/Core/Map/WorldMap.cs`
- Tile 存储：`src/Core/Map/MapTile.cs`
- 旧二进制加载占位：`src/Core/Systems/MapLoader.cs` 的 `LoadMapBinary`（LMAP）

## 7.2 差距清单

- 缺少 GridRegistry/多分辨率抽象：当前仅有单一 WorldMap 实例，无法表达 coarse/fine 并存与多份 grid 数据集。
- 缺少加载入口：LMAP 读取逻辑为占位，且未接入 MapConfig 的数据集声明。

## 7.3 迁移策略与风险

- 先落地文档与接口：明确 GridId/字段/单位/合并策略与 MapLoaded 注入契约。
- 再实现最小运行时容器（GridRegistry）与最小资产格式（JSON 或二进制均可），避免在“口径未定”阶段做性能优化。

# 8 验收条款

- 同一地图可同时提供 coarse 与 fine 两套 grid，并且 MapLoaded 上下文可用 GridId 定位到它们。
- 对同一 GridId 的多来源叠加结果必须可解释；无策略或不兼容时加载期直接失败并给出证据。
- 缺失/损坏资产必须在加载期失败，错误信息包含 GridId、来源与候选路径。
