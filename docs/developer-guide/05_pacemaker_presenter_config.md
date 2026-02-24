# Pacemaker, Presenter 与 ConfigPipeline 深度解析

本章深入探讨 Ludots 引擎的核心系统：时间管理 (Pacemaker)、视觉同步 (Presenter) 以及数据配置合并管线 (ConfigPipeline)。

## 1. Pacemaker 系统 (心跳与时间管理)

`Pacemaker` 是该引擎中解耦 **物理时间 (Wall-clock Time)** 与 **逻辑模拟时间 (Simulation Time)** 的核心组件。它负责驱动游戏的主循环。

*   **代码位置**: `src/Core/Engine/Pacemaker/IPacemaker.cs`

### 1.1 核心机制

*   **Accumulator (累加器)**:
    *   使用 `_accumulator` 累积 `dt` (Delta Time)。
    *   当 `_accumulator >= Time.FixedDeltaTime` 时，执行一次或多次 `simulationGroup.Update()` (即 FixedUpdate)。
    *   **作用**: 保证逻辑更新频率固定（如 60Hz），不受渲染帧率波动影响。
*   **Time Slicing (时间分片/预算控制)**:
    *   支持 `ICooperativeSimulation` 接口。如果单帧逻辑计算耗时超过 `timeBudgetMs`，Pacemaker 会暂停当前逻辑帧，在下一帧继续执行（"BudgetFuse" 机制）。
    *   **作用**: 防止长时间卡顿导致的帧率暴跌。
*   **Interpolation (插值)**:
    *   提供 `InterpolationAlpha` (0.0 - 1.0)，用于表现层在两个物理帧之间进行平滑插值渲染。

### 1.2 实现类

*   **RealtimePacemaker**: 标准的实时游戏循环，适用于动作游戏。
*   **TurnBasedPacemaker**: 回合制循环，仅在手动触发 `Step()` 时推进。

## 2. Presenter 系统 (视觉同步与 UI 绑定)

`Presenter` 模式用于隔离 **Core (纯逻辑/状态)** 与 **Platform (Unity/Godot 渲染)**。Core 计算“应该长什么样”，Presenter 将其转换为平台 API 调用。

*   **代码位置**: `src/Core/Presentation/`

### 2.1 工作流

1.  **Core State**: 纯数据对象（如 `CameraState`），包含逻辑坐标、目标 ID 等。
2.  **Presenter**:
    *   读取 Core State。
    *   执行纯数学计算（如球面坐标转换、平滑阻尼 `Lerp`）。
    *   **作用**: 将逻辑数据转换为适合渲染的数据（如 `Vector3`）。
3.  **Adapter (Interface)**:
    *   Presenter 调用 `ICameraAdapter` (或类似接口)，将计算好的 `Vector3` 传给引擎。
    *   这使得 Core 和 Presenter 不需要引用 Unity/Godot 的 GameObject。

### 2.2 响应链 (Response Chain) 中的应用

*   Presenter 被视为“表演者 (Performer)”。
*   Core 发出 `PresentationCommandBuffer` (如 "PlayAnimation", "ShowWindow")。
*   Presenter 接收指令并调用平台 UI/特效接口进行播放。

## 3. ConfigPipeline (数据配置合并管线)

`ConfigPipeline` 负责在游戏启动时加载并合并 `game.json` 配置片段。它允许 Mod 覆盖或扩展核心配置，而无需修改核心文件。

*   **代码位置**: `src/Core/Config/ConfigPipeline.cs`

### 3.1 合并策略 (Merge Strategies)

1.  **对象 (Objects/Dictionaries)**: **递归合并 (Deep Merge)**
    *   如果 Key 存在，则递归调用 `DeepMerge`。
    *   如果 Key 不存在，则克隆并添加。
    *   **结果**: 子属性会被合并，而不是替换。

2.  **数组 (Arrays) & 标量 (Scalars)**: **直接替换 (Replace)**
    *   **注意**: 当前实现中，**不存在** `ArrayById` 或数组追加策略。
    *   代码明确注释：`// Scalars or arrays - source overwrites target`。
    *   源配置中的数组会完全覆盖目标配置中的同名数组。

3.  **优先级 (Priority)**:
    *   合并顺序：Core (默认) -> Mod (按 Priority 升序)。
    *   高优先级的 Mod 配置会覆盖低优先级的配置。

### 3.2 示例

**Core (game.json)**:
```json
{
  "Settings": { "Volume": 100, "Quality": "High" },
  "Levels": [ "Level1", "Level2" ]
}
```

**Mod (game.json)**:
```json
{
  "Settings": { "Volume": 50 },  // Volume 变为 50，Quality 保持 High
  "Levels": [ "MyLevel" ]        // Levels 完全替换为 ["MyLevel"]
}
```

**合并结果**:
```json
{
  "Settings": { "Volume": 50, "Quality": "High" },
  "Levels": [ "MyLevel" ]
}
```
