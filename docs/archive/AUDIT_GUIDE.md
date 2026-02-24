# 脚本与 Mod 架构重构审计指南 (Audit Guide)

本指南旨在帮助开发者审查本次架构重构的核心变更，确保代码质量与设计一致性。

## 1. 核心架构变更 (Core Architecture Changes)

### 1.1 异步执行模型 (Async Execution Model)
- **目标**: 解决脚本执行中的线程安全问题与时间控制问题。
- **关键组件**:
  - `GameSynchronizationContext`: 实现了自定义同步上下文，强制 `await` 回调在游戏主线程执行。
  - `GameTask`: 替代 `Task.Delay`，提供基于游戏 Tick 的等待 (`Delay`, `NextFrame`, `WaitUntil`)。
  - **审查点**: 检查所有 `async void` 方法（除了事件处理器）是否被避免，确保所有异步路径都通过 `GameTask` 调度。

### 1.2 强类型地图系统 (Type-Safe Map System)
- **目标**: 消除 JSON 配置中的魔法字符串，利用 C# 类型系统保障元数据安全。
- **关键组件**:
  - `MapDefinition`: 地图元数据的基类，代码化定义 ID、Tags 和依赖。
  - `MapTag`: 强类型标签结构体。
  - `MapManager`: 升级为支持类扫描与注册。
  - **审查点**: 确认所有新地图都继承自 `MapDefinition`，且 JSON 仅包含纯数据（如地形）。

### 1.3 统一 Trigger 与 Hook 机制
- **目标**: 提供统一的逻辑容器与扩展能力。
- **关键组件**:
  - `Trigger`: 现为具体类，包含 `Actions` 列表，支持 `ExecuteAsync`。
  - `TriggerBuilder`: 提供流式 API 构建 Trigger。
  - `AnchorCommand`: 用于定义逻辑插入点（Hook Anchor）。
  - **审查点**: 检查 Trigger 是否正确使用了 `OnAnchor` 或 `InsertAfter` 进行扩展，而非硬编码修改。

## 2. 关键文件审查清单

| 文件路径 | 职责 | 审查重点 |
| :--- | :--- | :--- |
| `src/Core/Engine/GameSynchronizationContext.cs` | 线程调度 | 确保 `Post` 方法将回调推入主线程队列。 |
| `src/Core/Engine/GameTask.cs` | 异步等待 | 检查 `Delay` 是否扣除 `Time.DeltaTime` (受 TimeScale 影响)。 |
| `src/Core/Scripting/Trigger.cs` | 逻辑容器 | 检查 `ExecuteAsync` 是否顺序执行 `Actions`。 |
| `src/Core/Commands/GameCommand.cs` | 命令基类 | 确认签名为 `Task ExecuteAsync(ScriptContext)`。 |
| `src/Core/Map/MapDefinition.cs` | 地图元数据 | 确认 `Id` 默认取类名，`Tags` 为强类型列表。 |

## 3. 破坏性变更与迁移 (Breaking Changes)

- **GameCommand**: `Execute(GameContext)` 已移除，必须实现 `ExecuteAsync(ScriptContext)`。
- **Trigger**: `Execute(ScriptContext)` 已移除，必须实现 `ExecuteAsync(ScriptContext)`。
- **Context**: `ScriptContext` 现在是主要的上下文传递对象，包含 `Engine`, `World` 等核心系统的引用。
- **ActionRegistry**: 建议废弃，改用 `TriggerManager` 注册单例 Trigger。

## 4. 工具链更新 (Tooling Updates)

### 4.1 ModLauncher
- **EntryMap**: 移除了 `EntryMapId` 的配置项。`EntryMap` 现在是固定的系统入口，不再允许用户通过 Launcher 配置。
- **配置**: `game.json` 仅保留 `ModPaths`，`EntryMapId` 字段已被移除。

## 5. 代码规范 (Coding Standards)

- **禁止**: 在游戏逻辑中使用 `Task.Delay` (会导致逻辑脱离游戏时间)。
- **禁止**: 在 `Trigger` 中使用 `async void` (会导致异常无法捕获)。
- **推荐**: 使用 `TriggerBuilder` 组装简单逻辑。
- **推荐**: 使用 `MapTag` 静态字段定义标签，而非字符串字面量。
