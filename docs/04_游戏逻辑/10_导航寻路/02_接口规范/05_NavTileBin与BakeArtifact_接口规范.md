---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - NavTileBin 与 BakeArtifact
状态: 草案
依赖文档:
  - docs/04_游戏逻辑/10_导航寻路/01_架构设计/04_CDTNavMeshTileBake_架构设计.md
---

# NavTileBin与BakeArtifact 接口规范

# 1 概述
## 1.1 目标
- 定义 NavTile 的二进制落盘格式 NavTileBin 与失败回放工件 BakeArtifact 的字段形状与版本化口径。
- 任何运行时加载必须对 magic、formatVersion、buildConfigHash 与 checksum 做强校验，失败必须 fail-fast。

## 1.2 坐标与单位
- NavTile 顶点以 tile-local 厘米坐标存储：`(xCm,zCm,yCm)` 均为 int32。
- `OriginCm` 为该 tile 的世界偏移（cm），输出世界坐标时做 `world = origin + local`。

# 2 NavTileBin
## 2.1 文件头
字段顺序与字节序：
- 小端序。

Header:
- `Magic`：4 bytes，固定为 `NTIL`。
- `FormatVersion`：uint16。
- `Flags`：uint16（预留）。
- `TileId`：int32 chunkX，int32 chunkY，int32 layer。
- `TileVersion`：uint32（单调递增）。
- `BuildConfigHash`：uint64（稳定哈希）。
- `Checksum`：uint64（内容校验，覆盖 header 后续内容，算法由实现固定）。
- `OriginXcm`：int32。
- `OriginZcm`：int32。

## 2.2 主体数据段
所有数组均为长度前缀格式：
- `VertexCount`：int32
- `Vertices`：重复 `VertexCount` 次：`int32 xCm, int32 yCm, int32 zCm`

- `TriangleCount`：int32
- `Triangles`：重复 `TriangleCount` 次：`int32 a, int32 b, int32 c`

- `TriangleNeighborCount`：int32，必须等于 `TriangleCount`
- `TriangleNeighbors`：重复 `TriangleCount` 次：`int32 n0, int32 n1, int32 n2`（无邻接为 -1）

- `PortalCount`：int32
- `Portals`：重复 `PortalCount` 次：
  - `uint8 side`（0=West,1=East,2=North,3=South）
  - `int16 u0, int16 v0, int16 u1, int16 v1`（tile-local 网格点坐标）
  - `int32 leftXcm, int32 leftZcm, int32 rightXcm, int32 rightZcm`（tile-local）
  - `int32 clearanceCm`（可通行半径上限，cm）

## 2.3 不变量
- `VertexCount > 0` 时，所有 triangle 索引必须在 `[0,VertexCount)`。
- `TriangleNeighborCount == TriangleCount`。
- checksum 失败或版本不匹配必须加载失败，不允许静默忽略。

# 3 BakeArtifact
## 3.1 触发条件
- 任意阶段失败或显式降级必须输出 BakeArtifact。

## 3.2 内容字段
建议为 JSON 或二进制均可，但字段语义必须一致：
- `TileId`、`TileVersion`、`BuildConfig`（可序列化配置）
- `Stage`（枚举）：Walkability、Contour、PolygonProcess、Triangulate、Adjacency、Portal
- `ErrorCode`（枚举）与 `Message`（短字符串）
- `TriWalkMaskSummary`：walkable tri 数量、blocked 原因统计
- `Rings`：整数点序列与有向面积摘要
- `PolygonSet`：outer 与 holes 的归属关系
- `TriMeshSummary`：vertex/tri 计数与退化过滤计数

## 3.3 可回放要求
- BakeArtifact 必须包含能重建该 tile 输入的必要摘要或引用：输入层 hash、配置 hash、关键阈值。
- 回放工具必须能在不依赖运行时地图加载的情况下复现该 tile 的流水线并对比阶段输出。
