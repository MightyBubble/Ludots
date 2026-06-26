# 导航域统一 Epic 汇报

本页汇总 [Epic #281](https://github.com/MightyBubble/Ludots/issues/281) 下导航域统一重构的落地口径。权威目标仍以 Epic 与各 NAV 子单为准；本页面向评审、接手开发者与 Mod 作者，说明当前主线应该怎样理解新导航架构。

## 目标回顾

Epic #281 的目标是让一张 Ludots 地图在同一套生产链路下支持：

- Grid / Hex / NodeGraph 并存的 board 拓扑。
- 地形、障碍、area id、agent profile、bake 参数数据驱动。
- 编辑器 Bridge 与 CLI 共用真实 `NavBakeContext` / `NavBakeService`。
- 小队走精确 route / navmesh / road，上万单位走 MassFlow 执行，按 profile 策略共存。
- 删除旧二维导航执行栈，不引入 fallback、旁路 loader 或重复数据源。

目标架构的三段数据流是：

```mermaid
flowchart LR
    Authoring["Authoring<br/>Board + LogicTerrainField + Obstacle SSOT + AgentProfile"]
    BakeRoute["Bake / Route<br/>NavBakeContext + NavBakeService + PathServiceRouter"]
    Execution["Execution<br/>MassFlow / MassCrowd 唯一移动执行"]

    Authoring --> BakeRoute
    BakeRoute --> Execution
```

## 已落地的主线形态

| 领域 | 新 owner / 入口 | 说明 |
|---|---|---|
| 尺度 | `SpatialScaleDefaults`、`WorldExtentSpec`、`BoardConfig.WidthInMacroTiles` / `HeightInMacroTiles` | `CellCm` 是唯一 cm 基准；`MacroTileCells` 引用 `MapTile.Size`；编辑器可让作者填米数，但 MapConfig 存派生的 MacroTile 数量。 |
| 地形 | `LogicTerrainField` | Grid / Hex 共享逻辑地形抽象；视觉高度图只经显式投影 adapter 进入逻辑地形，不再隐式决定可走性。 |
| 障碍 | `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState` | bake 与执行消费同一套障碍数据；不新增 `ObstacleGeometryProfile2D` 或私有 obstacles loader。 |
| Agent | `agent_profiles.json` / AgentProfile registry | navmesh、pathing、MassFlow、road 通过 profile id 对齐 radius / height / clearance / mass / layer。 |
| Bake | `NavBakeContext` + `NavBakeService` | CLI、Bridge、runtime incremental 共用同一参数对象；Recast / CDT 都走统一服务；缺参 fail-fast。 |
| Route | `PathServiceRouter` / `AutoPathService` / `pathing.json` | `AutoCheapest`、`PreferGraph`、`PreferMesh` 数据驱动选择路由域。 |
| Execution | MassFlow / MassCrowd | 移动执行 sink 统一到 MassFlow；旧二维导航执行组件不再是新链路的目标。 |
| Editor | `Ludots.Editor.Bridge` + React editor | Web editor 走真实 Bridge：保存 MapConfig/terrain、估算/bake Recast、查询 Detour 路径、显示 navmesh/debug state。 |

## 子单收口矩阵

| 子单 | 主题 | 主线结果 |
|---|---|---|
| NAV-0 [#282](https://github.com/MightyBubble/Ludots/issues/282) | 空间尺度 SSOT + 常量模块 | `CellCm`、`MacroTileCells`、`TerrainChunkCells`、`FlowCell`、`AvoidanceHashCell` 等概念有单一命名和扫描测试。 |
| NAV-1 [#283](https://github.com/MightyBubble/Ludots/issues/283) | `WorldExtentSpec` 与 board 尺度 | 旧 `WidthInTiles` / `HeightInTiles` 语义正名为 `WidthInMacroTiles` / `HeightInMacroTiles`；旧键应 fail-fast。 |
| NAV-2 [#284](https://github.com/MightyBubble/Ludots/issues/284) | AgentProfile 收敛 | profile id 成为 navmesh/pathing/MassFlow/road 的跨层引用点。 |
| NAV-3 [#285](https://github.com/MightyBubble/Ludots/issues/285) | 障碍 SSOT | 障碍由 manifestation/shape/compound state 表达，bake 与执行共享。 |
| NAV-4 [#286](https://github.com/MightyBubble/Ludots/issues/286) | `LogicTerrainField` | Grid / Hex 地形输入可以走同一 bake 管线；视觉高度图和逻辑可走性分离。 |
| NAV-5 [#287](https://github.com/MightyBubble/Ludots/issues/287) | 一套 bake 参数 | `NavBakeContext` / `NavBakeService` 成为 CLI、Bridge、runtime incremental 的共同入口。 |
| NAV-6 [#288](https://github.com/MightyBubble/Ludots/issues/288) | MassFlow 吸收 Nav2D 独有能力 | MassFlow 承接逐 agent 目标、动态障碍与高质量避障扩展点。 |
| NAV-7 [#290](https://github.com/MightyBubble/Ludots/issues/290) | Route -> Execution 打通 | route/road/navmesh 产出的 waypoint 进入 MassFlow 执行 sink。 |
| NAV-8 [#289](https://github.com/MightyBubble/Ludots/issues/289) | 删除旧执行栈与 fallback | 旧二维导航执行栈不再作为新导航执行目标；清理 fallback 与幂等注册风险。 |
| NAV-9 [#303](https://github.com/MightyBubble/Ludots/issues/303) | 通用 move-plan seam | 从 road showcase 抽出通用 move-plan seam，road policy 留在 mod，执行 sink 指向 MassFlow。 |

## 配置与工具入口

| 入口 | 用途 |
|---|---|
| `gitbook/architecture/spatial-scale-and-resolution-ssot.md` | 空间尺度权威文档。 |
| `gitbook/reference/spatial-scale-configuration.md` | 快速查表：配置项、单位、owner、约束。 |
| `gitbook/reference/map-scale-authoring-guide.md` | Mod 作者地图尺度设计指南。 |
| `gitbook/reference/map-scale-authoring-starter.html` | 可交互 HTML 入门：输入目标米数与参数，派生 MacroTiles / cells / nav tiles / bake 预算。 |
| `gitbook/reference/navmesh-authoring-bake-toolchain.md` | 地形/障碍/area/agent/bake/editor/Raylib debug 工具链设计。 |
| `gitbook/reference/nav-bake-budget-and-estimation.md` | Bake 理论参数、预算与耗时估算。 |
| `gitbook/reference/nav-domain-configuration-migration-guide.md` | 旧配置迁移到新导航架构的操作手册。 |

## 关键约束

- `.ntil` 是 navmesh tile bake 产物，不是 authoring SSOT；不要手写、不要拿它当配置入口。
- 编辑器 New / Add Board 让作者输入目标米数和尺度参数，自动派生 `WidthInMacroTiles` / `HeightInMacroTiles`、grid cells、Terrain/NavTile 数量；用户不再填写 chunk 数量。
- Grid 的世界尺度由 `GridCellSizeCm` / `CellCm` 决定；HexGrid 的 `HexEdgeLengthCm` 只描述 hex 几何，不替代 `CellCm`。
- NodeGraph 是可并存 topology，不是 Grid/Hex 的互斥替代；road / waterway / lane 应作为 graph 内容与 route policy 表达。
- Recast 路径调试必须展示真实 Recast/Detour mesh 与查询来源，不能用假的绿色平面或自造路径算法冒充。
- 大 bake 不需要二次确认 checkbox；需要清晰的 estimate / progress / status，并由明确 Bake 动作承担显式执行语义。

## 验证命令

常用收口命令：

```powershell
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj --no-restore
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter NavigationSpatialScaleMagicNumberContractTests
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter BoardConfigTests
cd src/Tools/Ludots.Editor.React
npm run build
```

Web editor smoke：

1. 打开 `http://localhost:5173/`。
2. `New` 或 `Add Board` 输入目标米数，例如 `1000m x 500m`。
3. 预期预览显示 `1024m x 512m`、`1024 x 512 cells`、`4 x 2 MacroTiles`、`16 x 8 Terrain/NavTiles`。
4. 选择真实 mod/map/board 后，`Open`、`Save`、`Estimate`、`Bake`、`Simulation` 均应走 Bridge，不走脚本桩。

## 剩余风险与后续审计点

- HexGrid 运行时里仍存在无 board context 的 `HexCoordinates` 静态默认工具调用。新代码应优先使用带 `HexEdgeLengthCm` 的 board metrics / `HexMetrics`；老调用需要持续审计。
- 大地图编辑器性能仍要靠 LOD / dirty tile / incremental bake 控制，禁止回到全量重烤后清空旧 tile 的行为。
- NodeGraph 编辑、road/waterway authoring 与 Raylib 精准 debug view 需要继续按 `NavBakeContext` / route policy / MassFlow sink 的新边界实现。
