# 端到端验收用例 — Phase 1 / Phase 2a + Editor 坐标修复

**日期**: 2026-03-02
**关联**: Issue #3 (坐标断裂), Issue #4 (架构审计)

---

## A. Editor ↔ Raylib 地形管线验收

### A1: 编辑器创建地形 → Raylib 渲染一致

**前置**: Editor Bridge 运行 (port 5299), React Editor 运行 (port 5173), Raylib App 可启动

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Editor: 选择 TerrainBenchmarkMod → Load Repo | 地图加载，地形显示 |
| 2 | Editor: Height 工具，在 chunk(0,0) 右上角（约 col=50, row=10）画高度 15 的山峰 | 明显隆起，minimap 可见 |
| 3 | Editor: Water 工具，在山峰左侧（约 col=30, row=10）画水域 | 蓝色半透明水面 |
| 4 | Editor: Biome 工具，将山峰区域设为 Sand (id=2) | 颜色从默认变为沙色 |
| 5 | Editor: Save Repo | 保存成功，无报错 |
| 6 | Raylib: `dotnet run ... -- game.json` | 地图加载 |
| 7 | **验证**: 山峰位置、高度、沙色 biome 与 Editor 一致 | ✅ 几何一致 |
| 8 | **验证**: 水域位置和颜色与 Editor 一致 | ✅ 水面一致 |

**录屏要求**: 先在 Editor 完成编辑并截图，再在 Raylib 中找到相同区域截图，左右对比。

---

## B. Entity 坐标一致性验收 (Issue #3 修复)

### B1: Editor 放置实体 → Raylib 位置匹配

**前置**: Issue #3 修复已合入

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Editor: 选 TerrainBenchmarkMod → Load Repo | 地形加载 |
| 2 | Editor: Ent 工具，选 `moba_hero` 模板 | 模板选中 |
| 3 | Editor: 点击 hex (10, 5) 放置实体 | 方块出现在 hex(10,5) 对应位置 |
| 4 | Editor: 点击 hex (30, 20) 放置第二个实体 | 方块出现在 hex(30,20) |
| 5 | Editor: Save Repo | 保存成功 |
| 6 | **验证**: 检查保存的 JSON 中 Overrides.WorldPositionCm 值 | X/Y 应为 cm 值（非 hex 坐标） |
| 7 | Raylib: 启动并加载同一地图 | 地图加载 |
| 8 | **验证**: 两个实体在 Raylib 中的 3D 位置与 Editor 中的 hex 位置视觉一致 | ✅ 位置匹配 |

**坐标换算校验** (hex(10,5) → cm):
```
x_m = 6.928 * (10 + 0.5 * (5 & 1)) = 6.928 * 10.5 = 72.744m
z_m = 6.0 * 5 = 30.0m
WorldPositionCm = { X: 7274, Y: 3000 }
```

### B2: Engine 已有实体 → Editor 显示正确位置

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | 手动写 map JSON: 实体 WorldPositionCm = {X: 4157, Y: 1800} | 约 hex(6, 3) |
| 2 | Editor: Load Repo | 实体应显示在 hex(6, 3) 位置 |
| 3 | **验证**: Editor 中实体方块在地形上的位置正确 | ✅ 反向转换正确 |

### B3: 实体位置 roundtrip（Editor→保存→加载→Editor）

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Editor: 放置实体在 hex(15, 8) | 出现在对应位置 |
| 2 | Editor: Save Repo | 保存成功 |
| 3 | Editor: 重新 Load Repo | 实体仍在 hex(15, 8) |
| 4 | **验证**: roundtrip 不偏移 | ✅ 位置稳定 |

---

## C. Phase 1 — Board 抽象验收

### C1: HexGridBoard 地形加载

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | 配置 map JSON 包含 `Boards: [{ Type: "HexGrid", Id: "main", ... }]` | Board 配置生效 |
| 2 | Raylib: 启动加载 | 地形通过 HexGridBoard 加载，日志显示 Board 初始化 |
| 3 | **验证**: 地形渲染与无 Board 配置时一致 | ✅ 向后兼容 |

### C2: 日志系统验证

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Raylib: 启动应用 | 控制台输出带 `[INF]`/`[WRN]`/`[ERR]` 前缀和 Channel 标签的日志 |
| 2 | **验证**: 日志中可见 `[Engine]`, `[ModLoader]`, `[Map]` 等 Channel | ✅ 结构化日志 |
| 3 | **验证**: 如配置 FileLogging，检查日志文件生成 | ✅ 文件日志 |

### C3: MapConfig Board 配置后向兼容

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | 使用旧格式 map JSON（无 Boards 字段） | 正常加载，Boards 默认空列表 |
| 2 | 使用新格式 map JSON（含 Boards） | Board 正确创建 |
| 3 | **验证**: 新旧格式都能工作 | ✅ 兼容 |

---

## D. Phase 2a — Additive Map / Trigger 所有权验收

### D1: 基本 LoadMap 功能不退化

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Raylib: `game.json` 启动 | entry 地图加载 |
| 2 | **验证**: 地形、实体、相机全部正常 | ✅ 基本功能不退化 |
| 3 | **验证**: 日志显示 MapSession 创建和 Board 初始化 | ✅ 新架构生效 |

### D2: SystemFactoryRegistry 系统注册

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | Mod 通过 `context.SystemFactories.Register()` 注册系统工厂 | 注册成功 |
| 2 | Map trigger 调用 `TryActivate()` | 系统被创建并注册到引擎 |
| 3 | **验证**: 系统 Tick 被执行 | ✅ 系统激活正常 |

### D3: Map-scoped Trigger 隔离

| 步骤 | 操作 | 预期 |
|:--|:--|:--|
| 1 | 加载带 TriggerTypes 的地图 | Map triggers 注册 |
| 2 | FireMapEvent 只触发该地图的 triggers | 非此地图的 triggers 不触发 |
| 3 | 卸载地图后 triggers 被清理 | OnMapExit 被调用 |

---

## E. 回归测试

### E1: 全量单元测试

| 命令 | 预期 |
|:--|:--|
| `dotnet test src/Tests/GasTests/GasTests.csproj` | 所有测试通过（当前 3 个失败需先修复） |
| `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj` | 28 测试通过 |
| `dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj` | 1 测试通过 |

### E2: Editor Lint

| 命令 | 预期 |
|:--|:--|
| `cd src/Tools/Ludots.Editor.React && npx eslint .` | 无新增 lint 错误 |
| `cd src/Tools/Ludots.Editor.React && npx tsc -b --noEmit` | 无新增类型错误 |

---

## 录屏清单

| # | 场景 | 时长 | 内容 |
|:--|:--|:--|:--|
| V1 | 地形 E2E | ~60s | Editor 编辑山峰/水域 → Save → Raylib 加载对比 |
| V2 | 实体坐标 | ~45s | Editor 放置实体 → Save → Raylib 位置一致 |
| V3 | Roundtrip | ~30s | 放置 → 保存 → 重载 → 位置不偏移 |
| V4 | 日志系统 | ~15s | Raylib 启动日志显示结构化 Channel |
| V5 | 基本功能 | ~30s | Raylib 完整启动，地形+实体+相机运行 |
