# 最近提交审计与端到端交互验收

本文记录对近期关键提交的审计结果、修复落地，以及可玩交互的端到端验证路径。

## 1 审计范围

审计基线提交（按时间顺序）：

- `cd14ff8`：mod.json 解析统一 + 文档链接修复
- `933aebc`：Map 作为 Trigger 真相源 + LoadMap additive
- `51acbd8`：Board 抽象与空间域分离
- `6877dcc`：Map cleanup 作用域修复 + VertexMap 恢复修复

本次落地修复（本分支）：

- `a7d2d48`：修复 `AttributeAggregatorSystem` 构造不匹配，恢复 Core 编译
- `e519501`：`FireMapEvent` 兼容全局 trigger，同时避免 map-owned 重复触发
- `4e2652e`：补齐地图生命周期（实体挂起/恢复 + 导航恢复）
- `425623d`：Editor Bridge 对齐 Board 化 `MapConfig`
- `e220e3a`：Raylib Host 解除对 `Log.Backend`（internal）依赖
- `ac704ac`：新增 `AuditPlaygroundMod` + 交互演示配置

## 2 审计发现与修复矩阵

### 2.1 编译可用性（P0）

**问题**：`GameEngine` 调用了不存在的 3 参构造 `AttributeAggregatorSystem(...)`，导致 Core 直接编译失败。  
**修复**：回退为已存在构造 `AttributeAggregatorSystem(World)`。  
**验证**：`dotnet build src/Core/Ludots.Core.csproj -c Release -p:WarningLevel=0` 通过。

### 2.2 Trigger 兼容性（P0/P1）

**问题**：`FireMapEvent` 初始只执行 map-scoped triggers，历史上通过 `RegisterTrigger(MapLoaded)` 注册的全局 trigger 会静默失效。  
**修复**：

- `TriggerManager` 新增 map-owned trigger 跟踪集。
- `FireMapEvent` 执行集合改为：`map-scoped + 兼容全局（排除 map-owned）`。
- 保持优先级排序并防止重复触发。

**验证**：

- `Phase2InfrastructureTests` 新增兼容场景测试并通过。
- `--filter FullyQualifiedName~Phase2InfrastructureTests`：24/24 通过。

### 2.3 地图生命周期闭环（P1）

**问题**：

- Map push/pop 只有 session 状态变化，实体层未真正挂起/恢复。
- 外层地图恢复时，地形恢复存在但导航上下文恢复不完整。

**修复**：

- `GameEngine` 新增按 `MapId` 批量加/去 `SuspendedTag` 的实体状态切换。
- `LoadMap/PushMap`：挂起被切出的地图实体。
- `PopMap/UnloadMap`：恢复外层地图实体活跃状态。
- 恢复路径统一补齐 `LoadNavForMap(...)`。

**验证**：

- 新增 `SetMapEntitiesSuspended_*` 回归测试并通过。
- 结合交互演示，实体数随 Push/Pop 呈现可见变化（见第 4 节）。

### 2.4 工具链与配置模型一致性（P1）

**问题**：Editor Bridge 仍依赖旧 `MapConfig.DataFile/Spatial` 字段，与 Board 化模型漂移。  
**修复**：

- 以 `Boards[*].DataFile` 为地形数据来源（优先 `default` board，再 fallback 第一个含 DataFile 的 board）。
- `MergeMapConfig` 对齐 Core 语义：`Boards` 按名称覆盖/追加，补齐 `TriggerTypes` 合并。
- 移除旧字段引用。

**验证**：

- `dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release -p:WarningLevel=0` 通过。

## 3 六边形架构视角结论

### 3.1 正向

- Core 与 Raylib 适配层保持分层依赖关系（Adapter 依赖 Core）。
- 扩展端口（`SystemFactoryRegistry`、`TriggerDecoratorRegistry`）具备可运行路径。

### 3.2 风险与收敛

- 历史 Mod 仍大量使用 `IModContext.TriggerManager`（过渡期 API）。
- 本次通过兼容触发链 + `AuditPlaygroundMod` 演示，补齐“新旧共存”可运行证据。

## 4 可玩交互验收（AuditPlaygroundMod）

## 4.1 入口

- 配置：`src/Apps/Raylib/Ludots.App.Raylib/game.audit.json`
- Mod：`src/Mods/AuditPlaygroundMod`
- 地图：
  - `audit_outer`
  - `audit_inner`

## 4.2 操作说明

运行后在窗口内使用：

- `I`：Push inner map
- `O`：Pop map
- `P`：Reload outer map

左下角可见审计计数：

- `Audit Global`
- `Audit Scoped`
- `Audit Named`
- `Audit Anchor`
- `Audit Factory`

并可结合 `WorldPositionCm Entities` 观察实体数量变化。

## 4.3 验收预期

初始（outer 加载后）：计数约为 1，实体为 3。  
按 `I`：计数 +1，实体 3→5。  
按 `O`：计数不增加，实体 5→3。  
按 `P`：计数再 +1，实体保持/回到 outer 基线。

## 5 测试命令清单（本次执行）

```bash
dotnet build src/Core/Ludots.Core.csproj -c Release -p:WarningLevel=0
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "FullyQualifiedName~Phase2InfrastructureTests"
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "FullyQualifiedName~MapLoadCleanupTests"
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --filter "FullyQualifiedName~MapManagerInheritanceTests"
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release -p:WarningLevel=0
dotnet build src/Mods/AuditPlaygroundMod/AuditPlaygroundMod.csproj -c Release -p:WarningLevel=0
dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -p:WarningLevel=0
```

