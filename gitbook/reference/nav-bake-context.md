# NavBakeContext 与统一烘焙服务

所属总单：[Epic #281](https://github.com/MightyBubble/Ludots/issues/281)。本页落实 [NAV-5 #287](https://github.com/MightyBubble/Ludots/issues/287)，并回链 [NAV-0 #282](https://github.com/MightyBubble/Ludots/issues/282)、[NAV-2 #284](https://github.com/MightyBubble/Ludots/issues/284)、[NAV-3 #285](https://github.com/MightyBubble/Ludots/issues/285)、[NAV-4 #286](https://github.com/MightyBubble/Ludots/issues/286)。

## 背景（现状）

修改前，导航烘焙工具有三条入口和多套参数：

| 入口 | 旧行为 | 问题 |
|---|---|---|
| CLI `nav bake` | 直接读 `.vtxm`，调用 `NavTileBuilder` | 不经过统一 profile/obstacle 配置 |
| CLI `nav bake-react` | React `map_data.bin` 转 `.vtxm` 后本地烘焙 | 与编辑器端点重复 target 解析和并行循环 |
| CLI `nav bake-recast-react` | 读取 `Navigation/navmesh.json` 后手写 Recast 循环 | 与 Bridge 端点重复 |
| Bridge `/api/nav/bake-react` | 表单参数驱动 `BakePipeline` | 旧 CDT pipeline 含 GridMesh fallback |
| Bridge `/api/nav/bake-recast-react` | 表单参数驱动 `RecastNavTileBaker` 并写盘 | 与 CLI 参数和输出逻辑分叉 |

后果是有头与无头可能产物不同，错误会被 CDT -> GridMesh fallback 掩盖，来源路径也容易退化为本机绝对路径口径。

## 目标（预期）

`NavBakeContext` 是唯一烘焙请求对象，`NavBakeService` 是唯一执行入口。CLI 与编辑器 Bridge 只负责把命令行参数或 multipart 表单转成同一个 context。

In scope：

- `NavBakeContext` 聚合 map/profile/layer/obstacle/terrain/targets/build config/source URI。
- `NavBakeService` 通过 `INavBakeAlgorithm` 调用具体算法 adapter。
- 离线默认算法是 `recast`；`cdt` 是显式算法 adapter。
- `Navigation/navmesh.json` 必须显式声明 `mode` 与 `algorithm`，大小写严格。
- 删除 `BakePipeline` 的 CDT -> GridMesh fallback，CDT 失败即返回失败 artifact。
- CLI 与 Bridge 共用 `NavBakeTileSelection` 解析 dirty/full targets。

Out of scope：

- `runtime-incremental` 模式只在 service 形状中预留；CDT 脏块局部重建归 [NAV-10 #304](https://github.com/MightyBubble/Ludots/issues/304)，不阻塞 NAV-5。
- 不改变玩法运行时寻路/执行。

## User Story

US-5.1：作为关卡策划，我要编辑器与命令行用同一套参数烤同一张图，以便本地预览与 CI 产物一致。

Given 同一份 `NavBakeContext`；When CLI adapter 与 Bridge adapter 调用 `NavBakeService`；Then 逐 tile 二进制输出一致。

US-5.2：作为开发者，我要算法显式配置且缺失即报错，以便没有静默 fallback 掩盖错误。

Given `Navigation/navmesh.json`；When `algorithm` 缺失或写成错误大小写；Then loader fail-fast。

## UAT Showcase（钉死）

启动：`.\\scripts\\run-mod-launcher.cmd cli launch nav_bake --adapter raylib`

工具走真实生产链路：

| 命令 / 操作 | 可见反馈 |
|---|---|
| `dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav bake-recast-react --mapId <mapId> --in <map_data.bin> --dirty <dirty.json> --artifact true` | CLI 打印 `ok=<N> fail=0`，产出 `assets/Data/Nav/<mapId>/layer0/profile_<profile>/.../*.ntil` |
| Bridge `POST /api/nav/bake-recast-react`，字段与 CLI 相同 | 返回 `okCount` / `failCount` / tile base64；同一 tile 与 CLI 输出一致 |
| 修改 `Navigation/navmesh.json` 中 agent `maxClimbCm` 后重烤 | 产物 hash 改变，showcase HUD 显示重烤后的 tile hash |
| 删除 `algorithm` 后重烤 | loader 报错 `NavMeshBakeConfig must explicitly define 'algorithm'`，不产出 GridMesh 兜底结果 |

## 配置指南

`assets/Configs/Navigation/navmesh.json`：

| 字段 | 值 | 归属 | 约束 |
|---|---|---|---|
| `mode` | `offline` 或 `runtime-incremental` | `NavBakeContext.Mode` | 必填，大小写严格；NAV-5 仅实现 `offline` |
| `algorithm` | `recast` 或 `cdt` | `NavBakeContext.Algorithm` | 必填，大小写严格；生产默认 `recast` |
| `profiles[].id` | AgentProfile id | `AgentProfileRegistry` | 必须已存在，大小写严格 |
| `profiles[].maxClimbCm` | cm | NavMesh profile | 必填数字 |
| `profiles[].maxSlopeDeg` | degrees | NavMesh profile | 必填数字 |
| `layers[].id` | layer id | Nav layer | 必填字符串 |
| `layers[].layer` | int | NavTile layer | 必填数字 |
| `areas[]` | area cost 表 | NavMesh area costs | 必须显式数组，可为空 |

`sourceUri` 必须是 VFS URI，例如 `Core:Maps/example.vtxm`。工具层可从临时上传文件构造 context，但服务层只记录 URI，不接受私有 loader fallback。

## 配置到行为联动

| 改动 | 行为 | 自动化 |
|---|---|---|
| `algorithm: recast` | 走 `RecastNavBakeAlgorithm` | `NavBakeServiceContractTests` |
| `algorithm: cdt` | 走 `CdtNavBakeAlgorithm` | `NavBakeServiceContractTests` |
| 缺失 `algorithm` 或大小写错误 | loader fail-fast | `NavBakeServiceContractTests.NavMeshBakeConfig_RequiresExplicitAlgorithmAndStrictCase` |
| CDT triangulation 失败 | 返回失败 artifact，不走 GridMesh | `NavBakeServiceContractTests.CdtBakePipeline_DoesNotFallbackToGridMesh` |
| 同一 context 由 headless/Bridge adapter 调用 | tile bytes 相同 | `NavBakeServiceContractTests.NavBakeService_RunsSingleContextForHeadlessAndBridgeAdapters` |

## 合并 / 复用

本步不合并外部分支。复用项：

- `ConfigPipeline` / `ConfigCatalogLoader` 继续加载 `Navigation/navmesh.json`。
- `AgentProfileRegistry` 作为几何 profile SSOT。
- `LogicTerrainField` 作为 grid/hex 逻辑地形输入。
- `NavObstacleSet` 来自 NAV-3 的 authoring obstacle SSOT。
- `RecastNavTileBaker` 作为 `INavBakeAlgorithm` adapter。

## DoD

- 数据驱动：`mode` / `algorithm` / profile / layer 都来自配置或 context。
- 无 fallback：CDT 失败即失败；配置缺失或大小写错误 fail-fast。
- 无重复数据源：CLI 与 Bridge 都走 `NavBakeService`。
- 大小写严格 fail-fast：`offline` / `runtime-incremental` / `recast` / `cdt` 都大小写严格。
- 附 contract test：`NavBakeServiceContractTests` 覆盖一致性、配置严格性、无 GridMesh fallback。
- 更新 GitBook：本页加入 reference 与 summary。
- 回链总单：本文回链 #281 与 #287。
