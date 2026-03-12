# O8: 超时自动通过

> 响应窗口打开后，若玩家在 GateDeadline 内未输入，系统自动执行 Pass（放弃响应），原效果继续结算（如：卡牌游戏计时器、RTS 战术暂停时限）。

---

## 机制描述

超时自动通过是响应窗口的兜底机制，防止玩家无响应导致游戏卡死。核心特征：
- **时间限制**：每个响应窗口有 GateDeadline（倒计时）
- **自动 Pass**：超时后系统自动写入 `PlayerChoice=0`（Pass）
- **继续结算**：原效果按默认路径结算

与其他机制的区别：
- vs O1 陷阱/反制：O8 是 O1 的超时分支
- vs O4 暂停选择：O4 可设置无限时长（GateDeadline=-1），O8 强制有时限

---

## 交互层设计

- **Trigger**: 无（时间触发）
- **SelectionType**: `None`
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/ResponseChainListeners/<listener_id>.json
// 接口定义: src/Core/Gameplay/GAS/Components/ResponseChainListener.cs
{
  "eventTagId": "Event.DamageProposed",
  "responseType": "PromptInput",
  "priority": 50,
  "responseGraphId": "Graph.Trap.Counter",
  "promptTagId": "UI.Prompt.TrapActivation",
  "gateDeadlineTicks": 180                // 3 秒（60 ticks/秒）
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// 超时处理由 System 层自动完成，Graph 无需特殊处理

// System 层逻辑（伪代码）
// src/Core/Gameplay/GAS/Systems/ResponseChainTimeoutSystem.cs
public void Update(float deltaTime)
{
    foreach (var entity in _responseWindowQuery)
    {
        var window = entity.Get<ResponseChainWindow>();
        var deadline = entity.Get<GateDeadline>();

        if (_currentTick > deadline.Tick)
        {
            // 超时，自动 Pass
            var blackboard = entity.Get<BlackboardIntBuffer>();
            blackboard.Set("PlayerChoice", 0);  // 0 = Pass

            FireEvent("ResponseWindowTimeout", entity);

            // 关闭窗口
            window.Phase = WindowPhase.Resolve;
        }
    }
}
```

Phase Listener 读取 PlayerChoice：
```
// Phase OnPropose Listener (priority=10, scope=Target)
Graph.Trap.Counter:
  ReadBlackboardInt         E[effect], "PlayerChoice" → I[0]

  // PlayerChoice: 0=Pass, 1=Negate, 2=Activate
  ConstInt                  0                          → I[1]
  CompareEqInt              I[0], I[1]                 → B[0]
  JumpIfFalse               B[0], @check_activate

@pass:
  // Pass 分支：不做任何事，原效果继续
  Jump                      @end

@check_activate:
  ConstInt                  2                          → I[2]
  CompareEqInt              I[0], I[2]                 → B[1]
  JumpIfFalse               B[1], @end

  // Activate 分支：激活陷阱
  LoadContextTarget         E[1]
  LoadContextSource         E[0]
  LoadConfigInt             "CounterEffectId"         → I[3]
  ApplyEffectDynamic        E[1], E[0], I[3]

@end:
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| GateDeadline | `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs` | ✅ 已有 |
| ResponseChainWindow | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| BlackboardIntBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| ResponseChainHumanOrderSourceSystem | `src/Core/Input/Interaction/ResponseChainHumanOrderSourceSystem.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ResponseChainTimeoutSystem | P1 | 当前无独立的超时检查 System；需在 EffectProposalProcessingSystem 中添加超时逻辑 |
| GateDeadline 集成到 ResponseChainListener | P1 | 当前 GateDeadline 仅用于 AbilityExec；需支持 ResponseChain 窗口 |

---

## 最佳实践

- **DO**: 为所有 PromptInput 类型的 Listener 设置合理的 GateDeadline（建议 120-300 ticks）。
- **DO**: 在 UI 层显示倒计时条，提醒玩家剩余时间。
- **DO**: 超时后发布 `ResponseWindowTimeout` 事件，便于统计和调试。
- **DON'T**: 不要设置过短的 GateDeadline（< 60 ticks），玩家无法反应。
- **DON'T**: 不要在 Graph 内检查超时（由 System 层自动处理）。
- **DON'T**: 不要允许无限等待（GateDeadline=-1）在 PvP 模式中使用。

---

## 验收口径

### 场景 1: 正常超时自动 Pass

| 项 | 内容 |
|----|------|
| **输入** | 敌方施放伤害效果，玩家有陷阱（GateDeadline=180 ticks），玩家不操作 |
| **预期输出** | 180 ticks 后自动 Pass，原效果正常结算，玩家受到伤害 |
| **Log 关键字** | `[ResponseChain] WindowOpened deadline=180` → `[ResponseChain] Timeout tick=180` → `[GAS] PlayerChoice=0 (Pass)` → `[GAS] ApplyEffect Damage target=<player>` |
| **截图要求** | 倒计时条归零，UI 关闭，玩家 HP 减少 |
| **多帧录屏** | F0: 窗口打开 → F180: 超时 → F181: 自动 Pass → F182: 原效果结算 |

### 场景 2: 玩家在超时前输入

| 项 | 内容 |
|----|------|
| **输入** | GateDeadline=180，玩家在 tick=60 时按 `1` 激活陷阱 |
| **预期输出** | 陷阱激活，原效果被取消，不触发超时 |
| **Log 关键字** | `[ResponseChain] PlayerInput tick=60 choice=2` → `[GAS] TrapActivated` → `[GAS] EffectSkipped` |
| **截图要求** | 陷阱效果播放，玩家 HP 不变 |
| **多帧录屏** | F0: 窗口打开 → F60: 玩家输入 → F61: 陷阱激活 → F62: 窗口关闭 |

### 场景 3: 无限等待（战术暂停）

| 项 | 内容 |
|----|------|
| **输入** | GateDeadline=-1（无限等待），玩家在 tick=500 时选择目标 |
| **预期输出** | 不触发超时，等待玩家输入 |
| **Log 关键字** | `[ResponseChain] WindowOpened deadline=-1 (unlimited)` → `[ResponseChain] PlayerInput tick=500` |
| **截图要求** | 无倒计时条，游戏暂停直到玩家输入 |
| **多帧录屏** | F0: 暂停 → F500: 玩家输入 → F501: 恢复 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O8_TimeoutAutoPass_NoInput_EffectProceeds()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500);

    AttachResponseChainListener(world, defender, eventTag: "damage_incoming",
        responseType: ResponseType.PromptInput, priority: 50,
        graphId: GraphIds.TrapCounter, gateDeadlineTicks: 180);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.Fireball");
    world.Tick(181);  // 超过 GateDeadline

    // Assert
    var playerChoice = GetBlackboardInt(world, defender, "PlayerChoice");
    Assert.AreEqual(0, playerChoice, "应自动 Pass");

    var defenderHp = GetAttribute(world, defender, "Health");
    Assert.Less(defenderHp, 500f, "原效果应正常结算");
}

[Test]
public void O8_TimeoutAutoPass_InputBeforeTimeout_NoTimeout()
{
    // 玩家在 tick=60 输入 → 不触发超时
    // Assert: PlayerChoice = 2 (Activate), defender HP = 500
}

[Test]
public void O8_TimeoutAutoPass_UnlimitedDeadline_NoTimeout()
{
    // GateDeadline=-1 → 等待 1000 ticks 仍不超时
    // Assert: window still open at tick=1000
}
```

### 集成验收
1. 运行 `dotnet test --filter "O8_TimeoutAutoPass"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o8_timeout_auto_pass_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o8_normal_timeout.png`
   - `artifacts/acceptance/response_window/o8_input_before_timeout.png`
   - `artifacts/acceptance/response_window/o8_unlimited_deadline.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o8_60frames.mp4`
   - 标注关键帧：窗口打开帧、倒计时归零帧、自动 Pass 帧、效果结算帧

---

## 参考案例

- **游戏王 Duel Links**: 连锁窗口有 3 秒倒计时，超时自动 Pass。
- **炉石传说**: 回合时间限制，超时后自动结束回合。
- **Dota 2**: 技能施放有短暂的取消窗口（如 TP），超时后自动执行。
