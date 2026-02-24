---
文档类型: 工具设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 工具链 - Navigation - NavGraphBaker
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/18_工具设计.md
  - docs/04_游戏逻辑/10_导航寻路/01_架构设计/03_NavGraph生成方案_架构设计.md
---

# NavGraphBaker 工具设计

# 1 背景与目标

## 1.1 背景问题

64km 大世界导航不能依赖运行时全量合图与隐式扩容：需要离线把“地图与地形/障碍口径”转成可流式加载、可版本化、可校验的导航产物，并且能对产物做确定性验证与回归。

## 1.2 设计目标

- 生成导航产物：NodeGraph（局部精确图）与 ChunkGraph（骨干/分块索引）等运行时可消费数据。
- 产物契约明确：格式、版本、落盘位置、命名规则与消费方式。
- 增量友好：支持按地图分区/按 chunkKey 重建，避免全量重烘。
- 可诊断：生成过程输出统计（节点数/边数/连通域/失败原因/耗时），支持追踪到输入分区。

# 2 工具边界与非目标

## 2.1 工具边界

- 负责“从输入几何/障碍口径”生成导航离线产物，并可校验与导出诊断信息。
- 不负责地图与地形真源生成；输入必须来自地图与地形子系统的可审计产物。
- 不负责运行时加载策略；只保证运行时能够按产物契约加载。

## 2.2 非目标

- 不提供 Unity Editor 扫描/烘焙工作流（如需可在后续工具中封装，本工具保持 CLI 形态）。
- 不在工具内引入业务语义（阵营/技能/动态封路规则由运行时 Overlay 表达）。

# 3 输入输出与产物契约

## 3.1 输入

输入由“地图与地形”提供，工具只做消费与转换：

- `{ProjectRoot}/assets/Data/Maps/{mapId}/*`（地图分块/地形/障碍静态口径，具体由地图与地形子系统定义）
- 可选：`--regions <list>` 或 `--chunks <range>`（限定重建范围）
- 可选：`--preset <name>`（导航生成 preset：分辨率、步长、连接规则等）

## 3.2 输出产物

默认输出根目录：

- `{ProjectRoot}/assets/Data/Navigation/{mapId}/`

产物命名规则（建议）：

- `nav.meta.json`：产物索引与版本（包含 mapId、生成参数摘要、chunkKey 范围、hash、schemaVersion）
- `chunkgraph.bin`：ChunkGraph（二进制）
- `nodegraph_{chunkKey}.bin`：按 chunkKey 分片的 NodeGraph（二进制，可按需生成）
- `report.json`：统计报告（节点/边/连通域/烘焙耗时/失败列表）

必须包含的版本字段：

- `schemaVersion`：产物结构版本（主版本不兼容时必须 fail-fast）
- `contentHash`：输入摘要 hash（用于增量与回归）

## 3.3 运行时消费方式

- 运行时以 `nav.meta.json` 为入口定位产物集合与版本信息。
- GraphWorld/LoadedView 按 corridor/LoadedChunks 决定加载哪些 `nodegraph_{chunkKey}.bin`。
- ChunkGraph 用于范围约束与跨 chunk 的粗路径规划。

# 4 命令与参数

## 4.1 命令列表

建议作为 `Ludots.Tool` 的子命令提供：

- `nav bake`：生成导航产物
- `nav validate`：校验产物（版本/结构/连通性/约束）
- `nav diff`：对比两次产物（hash/统计/连通域差异）

## 4.2 参数说明

通用参数：

- `--map <mapId>`：地图标识
- `--inDir <path>`：输入根目录（默认 `assets/Data/Maps`）
- `--outDir <path>`：输出根目录（默认 `assets/Data/Navigation`）
- `--preset <name>`：生成 preset（默认 `default`）
- `--regions <csv>` / `--chunks <csv>`：限定生成范围（可选）
- `--force`：允许覆盖输出（破坏性操作，默认禁用）

## 4.3 可复制示例

```bash
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav bake --map ExampleMap --preset default --outDir assets/Data/Navigation --force
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav validate --map ExampleMap --inDir assets/Data/Navigation
```

# 5 版本化与兼容性

## 5.1 版本号策略

- `schemaVersion` 采用 `MAJOR.MINOR`：
  - MAJOR 变化：不兼容，运行时与工具必须同时升级
  - MINOR 变化：兼容扩展，旧运行时可忽略新增字段（需明确可忽略性）

## 5.2 破坏性变更与迁移方式

- 破坏性变更必须通过显式命令迁移：
  - `nav migrate --from <v> --to <v>`
- 默认不做隐式迁移：版本不匹配直接失败并给出迁移指引。

# 6 失败策略与诊断信息

## 6.1 失败策略

- 输入缺失/不可读：失败
- 输出不可写/产物版本冲突：失败
- 产物不满足关键约束（例如节点超限、连通域异常、chunkKey 重叠）：失败

## 6.2 诊断信息

必须输出：

- mapId / preset / 输入摘要 hash / schemaVersion
- 每个 region/chunk 的统计（节点/边/耗时）
- 失败项列表（定位到 region/chunkKey + 失败原因）

# 7 安全与破坏性操作

## 7.1 默认行为

- 默认不覆盖任何已存在产物。
- 默认不删除旧产物（避免误删跨版本资源）。

## 7.2 保护开关与回滚方式

- `--force`：允许覆盖同名产物（必须显式指定）
- `nav clean --map <mapId> --outDir <path>`：显式清理输出目录（必须二次确认或提供 `--yes`）
- 推荐每次 bake 输出 `nav.meta.json` 与 `report.json`，用于回滚与定位差异。

# 8 代码入口

## 8.1 命令入口

建议入口：

- `src/Tools/Ludots.Tool/Program.cs`：CLI 入口（现有）
- `src/Tools/Ludots.Tool/Commands/Nav/*`：nav 子命令实现（建议）

## 8.2 关键实现与测试

建议测试：

- 产物 schema roundtrip（写入/读取一致）
- validate 覆盖：版本不匹配、缺失分片、连通域异常
- diff 覆盖：统计差异、hash 差异可定位

