---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 游戏逻辑 - 地图与地形 - Graph路网工具
状态: 草案
---

# Graph路网工具 使用手册

# 1 工具定位

- 用途：制作/编辑/校验路网 Graph 数据（道路/人行道等），支持分块、粗细层级与 overlay（封路/成本调整）。
- 输出：Graph 数据集资产（目标形态），可被地图加载阶段选择性加载并注入 MapLoaded 上下文。

# 2 典型用户故事

- 故事 1：地图作者绘制道路与路口
  - 作为“地图作者”，我可以在地图上放置路口节点与道路边，设置基础代价（baseCost）。
  - 我可以给道路打标签（例如 `highway`、`bridge`），用于不同单位/策略的过滤。
  - 我导出后，游戏加载该地图时自动加载 `graph_road`。

- 故事 2：关卡脚本增加施工封路（静态 overlay）
  - 作为“关卡脚本作者”，我可以在地图资产中为某些边写入封路/加权 overlay。
  - 游戏加载后 overlay 自动叠加到路网中。

- 故事 3：运行期动态封路（运行期 overlay）
  - 作为“系统开发者”，我可以通过实体属性绑定（GAS sinks）把某条边动态设为 Blocked 或提高成本。

# 3 后端接口（目标口径）

- GET GraphDataSet
  - 入参：GraphId、MapId 或资产路径
  - 出参：
    - 分块：chunkKey 列表 + 每块 NodeGraph 数据 + cross edges
    - 可选：coarse/fine 层与层间映射（MultiLayerGraph）
    - 可选：静态 overlay

- PUT/POST GraphDataSet
  - 入参：同上
  - 出参：写入结果与校验报告（节点/边/跨块引用/容量等）

# 4 校验与诊断

- 校验项：
  - cross edge 的 toChunkKey/toLocalNodeId 必须可解析
  - overlay edgeCount 与装配后的 edgeCount 兼容
  - 坐标单位为厘米（cm）
- 失败策略：
  - 错误必须包含 GraphId、chunkKey（若有关）、来源与证据路径。

# 5 注意事项

- 路网 Graph 与 GASGraph（技能图）不是同一概念；路网 Graph 归属 `src/Core/Navigation/*`。
