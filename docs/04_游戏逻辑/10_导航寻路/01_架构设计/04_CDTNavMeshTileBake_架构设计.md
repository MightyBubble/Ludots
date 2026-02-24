---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - CDT NavMesh TileBake
状态: 草案
依赖文档:
  - docs/04_游戏逻辑/10_导航寻路/01_架构设计/03_NavGraph生成方案_架构设计.md
  - docs/04_游戏逻辑/10_导航寻路/04_裁决条款/01_64km_分层导航与合图_裁决条款.md
  - docs/04_游戏逻辑/09_地图与地形/00_总览.md
---

# CDT NavMesh TileBake 架构设计

# 1 设计概述
## 1.1 本文档定义
- 本文档定义一套用于 64km×64km 大世界的 CDT NavMesh 生成与运行时消费口径。
- NavMesh 为 2D 平面导航域，几何运算统一在 XZ 平面；高度为离散输入，仅用于可行走判定与落点采样。

## 1.2 设计目标
- 分块与增量：以 tile 为最小烘焙与加载单元，支持局部更新与按需 streaming。
- 确定性与可诊断：同输入同配置得到同输出；任何失败必须产出可回放工件并 fail-fast。
- 分层消费：L0 portal 粗图，L1 corridor 显式范围，L2 tile 内精确路径。

## 1.3 非目标
- 不做 Recast 体素化。
- 不把业务语义写入 navmesh 产物，语义与改权走 overlay。

# 2 术语与坐标约定
## 2.1 术语
- Tile：NavMesh 烘焙与加载单元，默认与 `VertexChunk(64×64)` 对齐。
- TriWalkMask：tile 内三角形级可行走掩码，每个 cell 2 个三角形。
- Portal：tile 边界上的可通行区段，作为跨 tile 连接最小单位。
- NavTile：一个 tile 的 navmesh 产物，包含三角网、邻接与 portals。

## 2.2 坐标
- 拓扑键：使用整数格点坐标 `(col,row)` 与整数边端点，禁止浮点量化匹配。
- 几何坐标：运行时导航与存储以厘米为主单位，顶点以 tile-local cm 存储以降低大世界精度风险。
- 平面约定：所有左右判定、funnel 与点在三角形测试均以 XZ 平面进行。

# 3 输入与输出
## 3.1 输入真源
- 地形真源：`VertexMap/VertexChunk`。
- tile 输入范围：`(64×64 cells)` 需要 `65×65` 顶点 halo，保证边界一致与 portal 可判定。
- 三角面基底：使用与渲染一致的离散三角剖分模式作为拓扑基底，每个 cell 生成 2 个三角形并作为 Walkability 的最小判定单位。

编辑器对接：
- React 编辑器导出 `map_data.bin`（+ 可选 dirty chunk 列表），通过 `Ludots.Tool nav bake-react` 转换并烘焙为 NavTiles。

## 3.2 输出产物
- NavTileBin：每 tile 独立二进制文件，含 magic、formatVersion、tileId、buildConfigHash、checksum。
- BakeArtifact：失败与诊断工件，包含输入摘要、rings、polygon 归属、失败阶段与错误码。

# 4 生成流水线
## 4.1 Walkability
- 输入：tile 顶点层（Height、Water、Blocked、Ramp 与必要 flags）。
- 输出：TriWalkMask 与不可走原因统计。
- 规则：高度差阈值与坡道裁决配置化；水面与阻挡为强约束。
- 悬崖平直化：若地形提供悬崖拉直标记，则 Walkability 必须按“拉直后的边界”裁决可通行性，避免锯齿悬崖在 navmesh 中形成非预期缝隙。

## 4.2 ContourExtractor
- 实现：三角形边段集合与组环。
- 过程：
  - 对每个 walkable 三角形，若某条边对侧不可走或越界，则发射一条有向边段。
  - 构建多重邻接 `from -> List<to>`，按一致绕向组装 ring。
- 边界几何：若 Walkability 引入了悬崖平直化切边，ContourExtractor 必须能接收额外边段并保持环闭合与绕向一致。

## 4.3 PolygonProcessor
- 清理：去重、去共线、最小面积过滤、绕向统一。
- 洞归属：基于包含关系与有向面积。
- 自检与修复：检测自交与不合法洞关系，明确回退链并输出 artifact。

## 4.4 Triangulator
- 目标：对 outer+holes 做约束德劳内三角化，失败需显式降级并输出 artifact。
- 要求：三角化前进行简单性校验；三角化后过滤退化三角形并建立索引级邻接。

## 4.5 PortalBuilder
- 边界判定：在 tile 四边界枚举单位边段，判断两侧是否同时可走。
- 合并：连续可通行边段合并为 portal，计算 left/right 与 clearance。
- 匹配：以 `Side + IntEdge(u0,v0,u1,v1)` 作为 identity，保证相邻 tile 一致生成。

# 5 运行时消费
## 5.1 NavTileStore
- AOI 驱动加载与卸载，LRU 缓存。
- tileVersion 参与缓存失效与走廊校验。

## 5.2 分层寻路
- L0：portal 粗图跨 tile 规划，受 corridor 与预算约束。
- L1：corridor 生成 tile 集合与 portal 序列，记录降级原因并可审计。
- L2：tile 内三角邻接 A* 生成 portal strip，拼接后做全局 funnel 输出折线路径。

# 6 失败策略与验收条款
## 6.1 失败策略
- 任何阶段失败必须产出 BakeArtifact 并返回明确错误码。
- 禁止生成不可解释的坏网格与静默 fallback。

## 6.2 验收条款
- Bake 导出与运行时加载能组成闭环，版本不匹配 fail-fast。
- streaming 下 tile load/unload 与 corridor 规模固定上限并可审计。
- 增量更新只影响 dirty tile 与边界 portal，且可版本化热切换。
