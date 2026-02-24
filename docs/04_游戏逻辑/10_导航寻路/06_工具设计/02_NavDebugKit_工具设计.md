---
文档类型: 工具设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 工具链 - Navigation - NavDebugKit
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/18_工具设计.md
  - docs/04_游戏逻辑/10_导航寻路/02_接口规范/01_导航寻路_接口规范.md
---

# NavDebugKit 工具设计

# 1 背景与目标

## 1.1 背景问题

导航问题在大世界场景下往往呈现为“局部缺路/错路/抖动/预算超限/加载缺失”，如果没有统一的 dump 与可视化口径，很难定位是：

- 输入产物问题（NavGraph/ChunkGraph 不一致或版本错）
- 运行时加载问题（LoadedView/corridor 未覆盖）
- 策略问题（TraversalPolicy/Overlay 造成封路或改权异常）
- 预算问题（队列拥塞/熔断/超时）

## 1.2 设计目标

- 提供统一的导航诊断入口：可导出最小复现场景与关键状态快照。
- 提供可视化/统计信号：在不改变运行时口径的前提下辅助定位。
- 提供回归抓手：dump 可用于离线 validate/diff 与自动化回归。

# 2 工具边界与非目标

## 2.1 工具边界

- 诊断输出不作为运行时 SSOT；只用于定位与回归。
- dump 内容必须去耦合业务语义：只记录导航必要状态（请求、图版本、overlay 版本、corridor、统计）。

## 2.2 非目标

- 不提供完整 editor 工具链与地形/障碍编辑能力。
- 不承诺零开销；调试能力必须通过显式开关启用。

# 3 输入输出与产物契约

## 3.1 输入

- 运行时采样点：PathRequest/PathResult、GraphWorld/LoadedView 状态、TraversalPolicy/Overlay 句柄与版本。
- 可选：指定 entity 或起终点、指定时间窗口、指定 chunkKey 范围。

## 3.2 输出产物

默认输出目录（建议）：

- `{ProjectRoot}/artifacts/nav-debug/{sessionId}/`

产物（建议）：

- `snapshot.json`：摘要（mapId、time、schemaVersion、关键版本号、统计计数）
- `request.json`：单次请求（起终点/策略/预算/期望约束）
- `result.json`：结果（状态码、路径点、耗时、expanded、dropped、失败原因）
- `corridor.json`：corridor/loadedChunks 快照
- `overlay.bin`：overlay 数据（可选，仅当需要复现规则差异）
- `notes.md`：人工补充说明（可选）

## 3.3 运行时消费方式

- dump 产物用于离线工具：
  - `Ludots.Tool nav validate-dump`：校验 dump 结构与版本
  - `Ludots.Tool nav replay`：在离线环境复现并对比结果（可选）

# 4 命令与参数

## 4.1 命令列表

建议提供两类入口：

- CLI（离线处理 dump）：
  - `nav validate-dump`
  - `nav replay`
- 运行时控制台/调试菜单（生成 dump）：
  - `nav.dump`
  - `nav.overlay.dump`
  - `nav.stats`

## 4.2 参数说明

运行时命令（建议）：

- `nav.dump --entity <id>` 或 `nav.dump --from <x,y,z> --to <x,y,z>`
- `--durationMs <n>`：采样窗口
- `--outDir <path>`：输出目录
- `--includeOverlay`：包含 overlay 数据（默认 false）

CLI 命令（建议）：

- `nav validate-dump --inDir <path>`：校验 dump 完整性与版本
- `nav replay --inDir <path> --preset <name>`：重放与对比（可选）

## 4.3 可复制示例

```bash
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav validate-dump --inDir artifacts/nav-debug/<sessionId>
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav replay --inDir artifacts/nav-debug/<sessionId> --preset default
```

# 5 版本化与兼容性

## 5.1 版本号策略

- dump 使用独立 `schemaVersion`，与 nav 产物版本分开管理。
- dump 必须携带运行时关键版本号：Nav 产物版本、overlayVersion、policyVersion（若存在）。

## 5.2 破坏性变更与迁移方式

- dump schema 破坏性变更必须提供显式迁移命令：
  - `nav migrate-dump --from <v> --to <v>`
- 默认不做隐式迁移：版本不匹配直接失败并提示迁移方式。

# 6 失败策略与诊断信息

## 6.1 失败策略

- dump 目录不可写：失败
- 关键字段缺失/版本不匹配：失败
- replay 时依赖缺失（产物/overlay 不齐）：失败

## 6.2 诊断信息

必须输出：

- sessionId、schemaVersion、mapId、关键版本号集合
- dump 生成来源（entityId 或 from/to）、采样窗口与采样点数量
- replay 的对比结果（差异摘要 + 可定位字段）

# 7 安全与破坏性操作

## 7.1 默认行为

- 默认不启用任何运行时 dump；必须显式开关打开（debug build 或 dev 配置）。
- 默认不写入 overlay 数据（避免泄漏规则表与增大体积）。

## 7.2 保护开关与回滚方式

- 运行时开关：`NavDebug.Enabled=true`（建议配置项）
- `--includeOverlay`：显式包含 overlay（仅用于定位规则差异）
- dump 输出目录以 sessionId 分隔，避免覆盖；如需覆盖必须显式 `--overwrite`

# 8 代码入口

## 8.1 命令入口

建议入口：

- 运行时：`src/Core/Navigation/Debug/*`（建议新增）
- CLI：`src/Tools/Ludots.Tool/Commands/Nav/*`（建议新增）

## 8.2 关键实现与测试

建议测试：

- dump schema roundtrip（写入/读取一致）
- validate-dump 覆盖缺字段/版本不匹配
- replay 在固定输入下 determinism 校验（相同输入得到相同路径或同等价结果）

