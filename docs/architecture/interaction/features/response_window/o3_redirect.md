# O3: 重定向

> 在效果结算前，玩家选择将效果的目标改为另一个实体（如：游戏王重定向陷阱、Dota Lotus Orb 反射）。

---

## 机制描述

重定向允许玩家在效果 Proposal 阶段修改 EffectContext.Target，使效果作用于新目标。核心特征：
- **目标替换**：不改变效果内容，只改变作用对象
- **选择新目标**：需要玩家输入或自动选择（如反射回施法者）
- **优先级结算**：在 OnPropose 阶段完成目标修改

与其他机制的区别：
- vs O5 Hook/取消：O3 不取消效果，只改变目标
- vs O6 Modify/修改：O6 修改数值，O3 修改目标实体

---

## 交互层设计

- **Trigger**: 无（被动触发）
- **SelectionType**: `Entity`（需选择新目标）或 `None`（自动重定向）
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/ResponseChainListeners/<redirect_id>.json
// 接口定义: src/Core/Gameplay/GAS/Components/ResponseChainListener.cs
{
  "eventTagId": "Event.DamageProposed",
  "responseType": "PromptInput",            // 需要玩家选择新目标
  "priority": 50,
  "responseGraphId": "Graph.Redirect.Manual",
  "conditionGraphId": "Graph.Redirect.Condition",
  "promptTagId": "UI.Prompt.RedirectTarget"
}
```

自动重定向（反射）：
```json5
{
  "eventTagId": "Event.SingleTargetAbilityProposed",
  "responseType": "Modify",                 // 自动修改目标
  "priority": 50,
  "responseGraphId": "Graph.Redirect.Reflect"
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// ResponseChain 系统: src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs

// 手动重定向（玩家选择新目标）
Graph.Redirect.Manual:
  // 假设玩家通过 SelectionGate 选择了新目标，存储在 Blackboard
  ReadBlackboardEntity      E[effect], "NewTarget" → E[2]
  WriteBlackboardEntity     E[effect], "EffectTarget", E[2]  // 修改目标

// 自动重定向（反射回施法者）
Graph.Redirect.Reflect:
  LoadContextSource         E[0]          // 原施法者
  LoadContextTarget         E[1]          // 原目标（持有反射的单位）
  WriteBlackboardEntity     E[effect], "EffectTarget", E[0]  // 目标改为施法者

// Phase OnPropose Listener (priority=50, scope=Target)
Phase OnPropose Listener (priority=50, scope=Target):
  ReadBlackboardEntity      E[effect], "EffectTarget" → E[2]
  // 检查新目标是否有效
  HasTag                    E[2], "Status.Untargetable" → B[0]
  JumpIfFalse               B[0], @apply_redirect
  // 无效目标，恢复原目标
  LoadContextTarget         E[1]
  WriteBlackboardEntity     E[effect], "EffectTarget", E[1]
  Jump                      @end
@apply_redirect:
  // 修改 EffectContext.Target（需要基建支持）
  // 当前通过 Blackboard 传递，EffectPhaseExecutor 读取
@end:
```

Effect 模板示例（反射护盾）：
```json5
{
  "id": "Effect.Ability.Redirect.ReflectShield",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "ticks": 300 },
  "grantedTags": ["Status.Reflecting"],
  "phaseListeners": [
    {
      "phase": "OnPropose",
      "priority": 50,
      "scope": "Target",
      "graphId": "Graph.Redirect.Reflect"
    }
  ]
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| ResponseChainListener | `src/Core/Gameplay/GAS/Components/ResponseChainListener.cs` | ✅ 已有 |
| BlackboardEntityBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| SelectionGate | `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs` | ✅ 已有 |
| GraphOps (WriteBlackboardEntity) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| EffectContext.Target 可变性 | P1 | 当前 EffectContext.Target 在 Proposal 阶段后不可修改；需支持 OnPropose Listener 修改目标 |
| SelectionGate 集成到 ResponseChain | P2 | 当前 SelectionGate 仅用于 AbilityExec；需支持在 ResponseChain 窗口内打开 SelectionGate |

---

## 最佳实践

- **DO**: 在 conditionGraphId 中检查新目标的有效性（距离、视野、Tag）。
- **DO**: 使用 Blackboard 传递新目标，避免直接修改 EffectContext（当前基建限制）。
- **DO**: 记录重定向日志（通过 SendEvent 发布事件）。
- **DON'T**: 不要在 Graph 内创建新实体或挂载组件（结构变更禁止）。
- **DON'T**: 不要重定向到无效目标（死亡、不可选中、超出范围）。
- **DON'T**: 不要在 responseGraphId 中执行副作用（如扣除资源），仅做目标修改。

---

## 验收口径

### 场景 1: 手动重定向（玩家选择新目标）

| 项 | 内容 |
|----|------|
| **输入** | 敌方对玩家 A 施放火球，玩家 A 有重定向能力，选择友方 B 作为新目标 |
| **预期输出** | 火球命中友方 B，玩家 A 不受伤 |
| **Log 关键字** | `[ResponseChain] PromptInput redirect` → `[GAS] NewTarget=<B_id>` → `[GAS] EffectTarget modified source=<A> target=<B>` |
| **截图要求** | UI 显示目标选择提示，火球飞向 B，B 的 HP 减少 |
| **多帧录屏** | F0: 火球提议 → F1: 重定向窗口打开 → F30: 玩家选择 B → F31: 目标修改 → F32: 火球命中 B |

### 场景 2: 自动重定向（反射回施法者）

| 项 | 内容 |
|----|------|
| **输入** | 敌方对玩家施放火球，玩家有反射护盾（自动重定向） |
| **预期输出** | 火球反射回敌方，敌方受到伤害，玩家不受伤 |
| **Log 关键字** | `[ResponseChain] Modify redirect priority=50` → `[GAS] EffectTarget modified target=<enemy>` → `[GAS] ApplyEffect Fireball target=<enemy>` |
| **截图要求** | 火球弹回敌方，敌方 HP 减少 |
| **多帧录屏** | F0: 火球提议 → F1: 反射 Listener 执行 → F2: 目标修改 → F3: 火球命中敌方 |

### 场景 3: 无效目标（重定向失败）

| 项 | 内容 |
|----|------|
| **输入** | 玩家尝试重定向到已死亡的单位 |
| **预期输出** | 重定向失败，效果作用于原目标 |
| **Log 关键字** | `[ResponseChain] RedirectFailed reason=InvalidTarget` → `[GAS] EffectTarget unchanged` |
| **截图要求** | 效果命中原目标 |
| **多帧录屏** | F0: 提议 → F1: 重定向尝试 → F2: 失败 → F3: 原目标受伤 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O3_Redirect_ManualSelection_CorrectTarget()
{
    // Arrange
    var world = CreateTestWorld();
    var enemy = SpawnUnit(world, hp: 500);
    var playerA = SpawnUnit(world, hp: 500);
    var playerB = SpawnUnit(world, hp: 500);

    AttachResponseChainListener(world, playerA, eventTag: "damage_incoming",
        responseType: ResponseType.PromptInput, priority: 50, graphId: GraphIds.RedirectManual);

    // Act
    world.SubmitOrder(enemy, OrderType.CastAbility, playerA, abilityId: "Ability.Fireball");
    world.Tick(1);  // Proposal

    // 模拟玩家选择 B 作为新目标
    InjectBlackboardEntity(world, playerA, "NewTarget", playerB);
    world.Tick(2);  // Redirect + Apply

    // Assert
    Assert.AreEqual(500f, GetAttribute(world, playerA, "Health")); // A 不受伤
    Assert.Less(GetAttribute(world, playerB, "Health"), 500f);     // B 受伤
}

[Test]
public void O3_Redirect_AutoReflect_BackToCaster()
{
    // 玩家有反射护盾 → 火球反射回敌方
    // Assert: enemy HP < 500, player HP = 500
}

[Test]
public void O3_Redirect_InvalidTarget_FallbackToOriginal()
{
    // 重定向到死亡单位 → 效果作用于原目标
    // Assert: original target HP < 500
}
```

### 集成验收
1. 运行 `dotnet test --filter "O3_Redirect"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o3_redirect_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o3_manual_selection.png`
   - `artifacts/acceptance/response_window/o3_auto_reflect.png`
   - `artifacts/acceptance/response_window/o3_invalid_target.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o3_60frames.mp4`
   - 标注关键帧：提议帧、重定向帧、效果命中帧

---

## 参考案例

- **游戏王 重定向陷阱**: 改变攻击目标到另一个怪兽。
- **Dota Lotus Orb**: 反射单体指向技能回施法者。
- **LoL Braum E**: 拦截飞行道具并改变目标（类似重定向）。
