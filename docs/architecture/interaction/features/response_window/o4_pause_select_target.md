# O4: 暂停选择目标

> 游戏暂停（或减速），玩家在响应窗口内选择受影响的目标（如：三国志战术暂停、XCOM 中断射击目标选择）。

---

## 机制描述

暂停选择目标允许玩家在效果结算前暂停游戏，选择或调整目标。核心特征：
- **时间控制**：暂停或减速游戏时间
- **目标选择**：玩家在暂停期间选择目标
- **恢复执行**：选择完成后恢复时间流逝

与其他机制的区别：
- vs P1 选择额外目标：O4 强调时间暂停，P1 是技能执行中的插入步骤
- vs O3 重定向：O4 是主动选择，O3 是响应式修改

---

## 交互层设计

- **Trigger**: 无（响应触发）
- **SelectionType**: `Entity`（选择目标）
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/ResponseChainListeners/<pause_select_id>.json
// 接口定义: src/Core/Gameplay/GAS/Components/ResponseChainListener.cs
{
  "eventTagId": "Event.TacticalPauseTriggered",
  "responseType": "PromptInput",
  "priority": 50,
  "responseGraphId": "Graph.PauseSelect.Execute",
  "promptTagId": "UI.Prompt.SelectTarget",
  "pauseSimulation": true                   // 暂停游戏时间
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// ResponseChain 系统: src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs

// 暂停选择目标流程
Graph.PauseSelect.Execute:
  // 1. 暂停时间（通过 System 层设置 TimeScale=0）
  // 2. 等待玩家选择（SelectionGate）
  ReadBlackboardEntity      E[effect], "SelectedTarget" → E[2]

  // 3. 验证目标有效性
  HasTag                    E[2], "Status.Selectable" → B[0]
  JumpIfFalse               B[0], @invalid_target

  // 4. 应用效果到选定目标
  LoadContextSource         E[0]          // 施法者
  LoadConfigInt             "EffectId" → I[0]
  ApplyEffectDynamic        E[0], E[2], I[0]
  Jump                      @end

@invalid_target:
  // 无效目标，取消效果
  WriteBlackboardInt        E[effect], "SkipMain", 1

@end:
  // 恢复时间（通过 System 层设置 TimeScale=1）
```

时间控制（System 层）：
```csharp
// src/Core/Gameplay/GAS/Systems/ResponseChainTimeControlSystem.cs
// 当 ResponseChainListener.PauseSimulation = true 时：
public void OnResponseWindowOpened(Entity responseEntity)
{
    if (HasComponent<PauseSimulation>(responseEntity))
    {
        _simulationTimeScale = 0f;  // 暂停
        FireEvent("SimulationPaused");
    }
}

public void OnResponseWindowClosed(Entity responseEntity)
{
    if (HasComponent<PauseSimulation>(responseEntity))
    {
        _simulationTimeScale = 1f;  // 恢复
        FireEvent("SimulationResumed");
    }
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| ResponseChainListener | `src/Core/Gameplay/GAS/Components/ResponseChainListener.cs` | ✅ 已有 |
| SelectionGate | `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs` | ✅ 已有 |
| GasSelectionResponseSystem | `src/Core/Input/Selection/GasSelectionResponseSystem.cs` | ✅ 已有 |
| BlackboardEntityBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| GraphOps (ApplyEffectDynamic) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 时间膨胀/暂停机制 | P2 | 当前无全局 TimeScale 控制；需在 GameEngine 或 SystemGroup 层实现 |
| ResponseChainListener.PauseSimulation 字段 | P2 | 需在 ResponseChainListene添加 PauseSimulation 标志位 |
| SelectionGate 集成到 ResponseChain | P2 | 当前 SelectionGate 仅用于 AbilityExec；需支持在 ResponseChain 窗口内打开 |

---

## 最佳实践

- **DO**: 在 conditionGraphId 中检查是否允许暂停（如 PvP 模式可能禁止暂停）。
- **DO**: 暂停期间禁用 AI 输入，仅允许人类玩家操作。
- **DO**: 记录暂停/恢复事件（通过 SendEvent 发布）。
- **DON'T**: 不要在 Graph 内直接修改 TimeScale（由 System 层控制）。
- **DON'T**: 不要在暂停期间执行 Tick 相关逻辑（如冷却倒计时）。
- **DON'T**: 不要允许无限暂停（设置合理的 GateDeadline）。

---

## 验收口径

### 场景 1: 战术暂停选择目标

| 项 | 内容 |
|----|------|
| **输入** | 玩家触发战术暂停，游戏暂停，玩家选择敌方单位 A 作为目标 |
| **预期输出** | 游戏暂停，UI 显示可选目标，玩家选择后游戏恢复，效果命中 A |
| **Log 关键字** | `[ResponseChain] PauseSimulation=true` → `[Simulation] TimeScale=0` → `[GAS] SelectedTarget=<A_id>` → `[Simulation] TimeScale=1` → `[GAS] ApplyEffect target=<A>` |
| **截图要求** | 暂停时所有单位静止，UI 显示目标高亮，恢复后效果命中 |
| **多帧录屏** | F0: 触发暂停 → F1: 游戏静止 → F30: 玩家选择 A → F31: 游戏恢复 → F32: 效果命中 A |

### 场景 2: 超时自动取消

| 项 | 内容 |
|----|------|
| **输入** | 玩家触发暂停，但在 GateDeadline 内未选择目标 |
| **预期输出** | 超时后游戏自动恢复，效果取消 |
| **Log 关键字** | `[ResponseChain] Timeout` → `[Simulation] TimeScale=1` → `[GAS] EffectSkipped reason=Timeout` |
| **截图要求** | 倒计时条归零，游戏恢复，无效果命中 |
| **多帧录屏** | F0: 暂停 → F180: 超时 → F181: 恢复 → F182: 无效果 |

### 场景 3: 选择无效目标

| 项 | 内容 |
|----|------|
| **输入** | 玩家选择不可选中的目标（如隐身单位） |
| **预期输出** 示重新选择或取消 |
| **Log 关键字** | `[GAS] InvalidTarget reason=Untargetable` → `[UI] PromptReselect` |
| **截图要求** | 目标高亮消失，提示文本显示 |
| **多帧录屏** | F0: 选择 → F1: 无效提示 → F2: 重新选择 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O4_PauseSelect_NormalSelection_EffectApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var player = SpawnUnit(world, hp: 500);
    var enemyA = SpawnUnit(world, hp: 500);
    var enemyB = SpawnUnit(world, hp: 500);

    AttachResponseChainListener(world, player, eventTag: "tactical_pause",
        responseType: ResponseType.PromptInput, priority: 50,
        graphId: GraphIds.PauseSelect, pauseSimulation: true);

    // Act
    world.FireEvent("tactical_pause", player);
    world.Tick(1);  // 暂停

    Assert.AreEqual(0f, world.GetTimeScale(), "游戏应暂停");

    // 模拟玩家选择 enemyA
    InjectBlackboardEntity(world, player, "SelectedTarget", enemyA);
    world.Tick(2);  // 恢复 + 效果应用

    // Assert
    Assert.AreEqual(1f, world.GetTimeScale(), "游戏应恢复");
    Assert.Less(GetAttribute(world, enemyA, "Health"), 500f);
}

[Test]
public void O4_PauseSelect_Timeout_EffectCancelled()
{
    // 超时未选择 → 效果取消
    // Assert: TimeScale = 1, no ct applied
}

[Test]
public void O4_PauseSelect_InvalidTarget_Reselect()
{
    // 选择无效目标 → 提示重新选择
    // Assert: SelectedTarget = null, UI prompt shown
}
```

### 集成验收
1. 运行 `dotnet test --filter "O4_PauseSelect"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o4_pause_select_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o4_normal_selection.png`
   - `artifacts/acceptance/response_window/o4_timeout.png`
   - `artifacts/acceptance/response_window/o4_invalid_target.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o4_60frames.mp4`
   - 标注关键帧：暂停帧、选择帧、恢复帧、效中帧

---

## 参考案例

- **三国志 战术暂停**: 战斗中暂停，选择技能目标后恢复。
- **XCOM 中断射击**: 敌人移动触发中断，玩家选择是否射击及目标。
- **Divinity: Original Sin 战术模式**: 回合制战斗中的暂停选择。
