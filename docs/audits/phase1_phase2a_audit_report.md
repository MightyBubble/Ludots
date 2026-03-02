# 审计报告：Phase 1 + Phase 2a 架构变更

**审计范围**：`51acbd8` (Phase 1) + `933aebc` (Phase 2a)，共 77 文件、+3800 行
**审计日期**：2026-03-02
**关联 Issue**: #4

---

## 一、回归测试结果

在 main 分支上跑全量 GasTests（624 个），**3 个测试失败**：

| 测试 | 错误 | 严重性 |
|:--|:--|:--|
| `GenerateGasProductionReport` | `TCG/Modify` 场景：Expected=80 Actual=70 | 🟡 GAS 数值逻辑变化 |
| `Culling_FarEntity_LowLOD` | Expected: `Low`, Actual: `Culled` | 🟡 CameraCulling 阈值/逻辑变更 |
| `Culling_MediumDistance_MediumLOD` | Expected: `Medium`, Actual: `Culled` | 🟡 同上 |

后两个是 **新增的 ThreeCSystemTests**，说明 CameraCullingSystem 的距离阈值或 FOV 计算与测试假设不一致。

---

## 二、Critical Bug（2 个）

### 🔴 C1: MapSession.Cleanup 误杀所有地图实体

**文件**：`src/Core/Map/MapSession.cs:92-98`

```csharp
world.Destroy(in _mapEntityQuery);  // WithAll<MapEntity>() — 无 MapId 过滤
```

`_mapEntityQuery` 匹配所有 `MapEntity`，不区分 MapId。**嵌套地图场景下，卸载内层地图会连带销毁外层地图的实体。**

**修复建议**：逐实体过滤 `MapEntity.MapId == session.MapId`，只销毁归属本 session 的实体。

### 🔴 C2: PopMap/UnloadMap 不恢复外层 VertexMap

**文件**：`src/Core/Engine/GameEngine.cs:1027-1052`

PopMap 恢复外层 session 时只调了 `ApplyBoardSpatialConfig()`，**没有恢复 `VertexMap` 和 `GlobalContext[VertexMap]`**。RaylibTerrainRenderer 依赖 `engine.VertexMap`，pop 后地形渲染为空。

**修复建议**：PopMap/UnloadMap 恢复时加：
```csharp
VertexMap = (primaryBoard as ITerrainBoard)?.VertexMap;
GlobalContext[ContextKeys.VertexMap] = VertexMap;
```

---

## 三、Warning 级问题（12 个）

### 架构安全

| # | 问题 | 文件 | 说明 |
|:--|:--|:--|:--|
| W1 | PushMap 不检查 MapSessions 是否初始化 | GameEngine.cs | LoadMap 前 PushMap 会 NRE |
| W2 | SystemFactoryRegistry 无 Deactivate | SystemFactoryRegistry.cs | 系统跨地图累积，卸载时不清理 |
| W3 | MapSessionManager.CreateSession 替换时不清 FocusStack | MapSessionManager.cs | 旧 session 残留在 stack 中 |
| W4 | NavMesh context 在 UnloadMap 时未清理 | GameEngine.cs | 恢复的地图可能看到前一张地图的 NavMesh |

### 线程安全

| # | 问题 | 文件 |
|:--|:--|:--|
| W5 | SystemFactoryRegistry 字典无锁 | SystemFactoryRegistry.cs |
| W6 | MapSessionManager 字典/栈无锁 | MapSessionManager.cs |
| W7 | TriggerManager FireEvent/FireMapEvent 迭代中可能并发修改 | TriggerManager.cs |
| W8 | TriggerDecoratorRegistry 字典无锁 | TriggerDecoratorRegistry.cs |
| W9 | Log.IsEnabled 读 _channelLevels 无锁且无越界检查 | Log.cs |

### 设计限制

| # | 问题 | 文件 |
|:--|:--|:--|
| W10 | HexGridBoard.NavServices 声明了但从未赋值 | HexGridBoard.cs |
| W11 | TriggerManager.OnMapEnter 只在 MapLoaded 事件时调用 | TriggerManager.cs:334 |
| W12 | GameEngine.Dispose() 不卸载地图/清理 Session | GameEngine.cs |

---

## 四、Good 设计（亮点）

| 方面 | 说明 |
|:--|:--|
| ✅ Board 接口分离 | `IBoard` / `ITerrainBoard` / `INavigableBoard` / `INodeGraphBoard` 遵循 ISP |
| ✅ MapContext 分层查找 | local → parent → root，支持嵌套地图上下文隔离 |
| ✅ LogInterpolatedStringHandler | 性能优化正确——日志关闭时不分配字符串 |
| ✅ FileLogBackend ConcurrentQueue | 写日志不阻塞主线程 |
| ✅ TriggerDecoratorRegistry | Anchor 插入反序避免索引偏移，设计合理 |
| ✅ Additive LoadMap | 支持地图叠加加载，架构方向正确 |
| ✅ Map-scoped Triggers | FireMapEvent 隔离机制清晰 |
| ✅ SystemFactoryRegistry 幂等激活 | TryActivate 重复调用安全 |
| ✅ MapConfig 后向兼容 | Boards/TriggerTypes 默认空列表，旧 JSON 不报错 |

---

## 五、建议修复优先级

| 优先级 | 项 | 工作量 |
|:--|:--|:--|
| **P0** | C1: MapSession.Cleanup 过滤 MapId | 小 |
| **P0** | C2: PopMap/UnloadMap 恢复 VertexMap | 小 |
| **P1** | W2: SystemFactoryRegistry 加 Deactivate | 中 |
| **P1** | W3: CreateSession 替换时清 FocusStack | 小 |
| **P1** | 修复 3 个失败测试 | 小-中 |
| **P2** | W5-W9: 线程安全（如果确认单线程可以推迟） | 中 |
| **P2** | W10-W12: 设计补全 | 各 小 |

---

## 六、测试覆盖评估

新增测试文件：
- `ThreeCSystemTests.cs` — 760 行，覆盖 Camera/Culling/VisualSync（2 个 Culling 测试失败）
- `Phase2InfrastructureTests.cs` — 434 行，覆盖 SystemFactory/MapSession/TriggerDecorator/Log

**测试质量**：覆盖了核心路径，但缺少：
- MapSession.Cleanup 的多地图并存场景测试
- PopMap 后状态恢复的测试
- SystemFactoryRegistry 的 map-unload 清理测试
