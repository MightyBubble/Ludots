# O2: 连锁响应

> 多个玩家/实体可依次响应同一效果，形成响应链；后续响应可覆盖或追加前序响应（如：游戏王连锁、万智牌堆栈）。

---

## 机制描述

连锁响应允许多个 ResponseChainListener 对同一效果提议进行嵌套响应。每个响应可以是 Hook（取消）、Modify（修改）或 Chain（追加新效果）。响应按 priority 升序执行，但效果结算按 LIFO（后进先出）顺序。

核心特征：
- **多层嵌套**：响应可触发新的响应窗口
- **LIFO 结算**：最后加入的响应最先结算
- **优先级排序**：priority 值决定响应顺序

与其他机制的区别：
- vs O1 陷阱/反制：O2 支持多层嵌套，O1 是单次决策
- vs O7 Chain/追加：O7 是单个 Listener 的行为，O2 是多个 Listener 的协作

---

## 交互层设计

- **Trigger**: 无（被动触发）
- **SelectionType**: `None`（或根据具体响应类型）
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/ResponseChainListeners/<listener_id>.json
// 接口定义: src/Core/Gameplay/GAS/Components/ResponseChainListener.cs
{
  "eventTagId": "Event.DamageProposed",
  "responseType": "Chain",                // Hook / Modify / Chain / PromptInput
  "priority": 100,                        // 越小越先执行
  "responseGraphId": "Graph.Chain.Reflect",
  "conditionGraphId": "Graph.Chain.Condition"
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// ResponseChain 系统: src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs

// 示例：玩家 A 施放火球 → 玩家 B 反射 → 玩家 A 再次反射

// 原始效果提议
Effect.Fireball (source=A, target=B, damage=100)
  → EffectProposalProcessingSystem.Collect 阶段

// 玩家 B 的响应 (priority=50)
ResponseChainListener (owner=B, priority=50, responseType=Chain):
  Graph.Chain.Reflect:
    LoadContextSource         E[0]          // A
    LoadContextTarget         E[1]          // B
    LoadConfigInt             "ReflectEffectId" → I[0]
    ApplyEffectDynamic        E[1], E[0], I[0]  // B 对 A 施加反射效果

// 玩家 A 的响应 (priority=100)
ResponseChainListener (owner=A, priority=100, responseType=Chain):
  Graph.Chain.Reflect:
    LoadContextSource         E[0]          // B（反射效果的施法者）
    LoadContextTarget         E[1]          // A
    LoadConfigInt             "ReflectEffectId" → I[0]
    ApplyEffectDynamic        E[1], E[0], I[0]  // A 对 B 施加反射效果

// 结算顺序（LIFO）：
// 1. A 的反射效果结算（最后加入，最先结算）
// 2. B 的反射效果结算
// 3. 原始火球效果结算（如果未被 Hook）
```

多层 Hook 示例：
```
// 玩家 A 施放火球 → 玩家 B Hook → 玩家 C Hook B 的 Hook

Phase OnPropose Listener (priority=50, owner=B, responseType=Hook):
  WriteBlackboardInt        E[effect], "SkipMain", 1  // 取消原效果

Phase OnPropose Listener (priority=100, owner=C, responseType=Hook):
  // C 取消 B 的 Hook（通过修改 Blackboard）
  WriteBlackboardInt        E[effect], "SkipMain", 0  // 恢复原效果
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| ResponseChainListener | `src/Core/Gameplay/GAS/Components/ResponseChainListener.cs` | ✅ 已有 |
| WindowPhase (Collect/WaitInput/Resolve) | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| ResponseType (Hook/Modify/Chain/PromptInput) | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GraphOps (ApplyEffectDynamic) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 嵌套 Chain 的递归 Proposal 处理验证 | P2 | 需确保 Chain 创建的新 EffectRequest 会再次走 Proposal 流程，避免无限递归 |
| ResponseChain 深度限制 | P2 | 防止恶意/错误配置导致的无限连锁（建议最大深度 10） |

---

## 最佳实践

- **DO**: 使用 priority 值明确响应顺序，避免依赖隐式执行顺序。
- **DO**: 在 conditionGraphId 中检查响应条件（如资源、冷却），避免无效响应。
- **DO**: 记录 Chain 深度，超过阈值时自动 Pass。
- **DON'T**: 不要在 responseGraphId 中创建循环依赖（A 响应 B，B 响应 A）。
- **DON'T**: 不要在 Graph 内直接修改 EffectContext.Target（使用 ResponseType.Modify）。
- **DON'T**: 不要依赖 UI 层状态做逻辑判断。

---

## 验收口径

### 场景 1: 双层连锁（反射 + 再反射）

| 项 | 内容 |
|----|------|
| **输入** | 玩家 A 对 B 施放火球（100 伤害），B 有反射（priority=50），A 有反射（priority=100） |
| **预期输出** | 结算顺序：A 的反射 → B 的反射 → 原始火球；最终 A 受到 100 伤害 |
| **Log 关键字** | `[ResponseChain] Collect priority=50 owner=B` → `[ResponseChain] Collect priority=100 owner=A` → `[GAS] Resolve LIFO: A.Reflect → B.Reflect → Fireball` |
| **截图要求** | 连锁动画依次播放，最终 A 的 HP 减少 |
| **多帧录屏** | F0: 火球提议 → F1: B 响应 → F2: A 响应 → F3: A 反射结算 → F4: B 反射结算 → F5: 火球结算 |

### 场景 2: Hook 取消连锁

| 项 | 内容 |
|----|------|
| **输入** | 玩家 A 施放火球，B 有 Hook（priority=50） |
| **预期输出** | B 的 Hook 设置 SkipMain=1，火球不结算 |
| **Log 关键字** | `[ResponseChain] Hook priority=50 owner=B` → `[GAS] EffectSkipped SkipMain=1` |
| **截图要求** | 无伤害浮字，B 的 HP 不变 |
| **多帧录屏** | F0: 火球提议 → F1: B Hook → F2: 火球跳过 |

### 场景 3: 多层 Modify（护甲 + 护盾）

| 项 | 内容 |
|----|------|
| **输入** | 玩家 A 对 B 施放 100 伤害，B 有护甲（priority=50，减伤 30%）和护盾（priority=100，吸收 20） |
| **预期输出** | 伤害计算：100 → 70（护甲）→ 50（护盾吸收 20）；B 受到 50 伤害 |
| **Log 关键字** | `[ResponseChain] Modify priority=50 armor` → `[ResponseChain] Modify priority=100 shield` → `[GAS] FinalDamage=50` |
| **截图要求** | 伤害浮字显示 50 |
| **多帧录屏** | F0: 伤害提议 → F1: 护甲 Modify → F2: 护盾 Modify → F3: 最终伤害结算 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O2_ChainResponse_DoubleReflect_CorrectOrder()
{
    // Arrange
    var world = CreateTestWorld();
    var playerA = SpawnUnit(world, hp: 500);
    var playerB = SpawnUnit(world, hp: 500);

    // B 有反射（priority=50）
    AttachResponseChainListener(world, playerB, eventTag: "damage_incoming",
        responseType: ResponseType.Chain, priority: 50, graphId: GraphIds.Reflect);

    // A 有反射（priority=100）
    AttachResponseChainListener(world, playerA, eventTag: "damage_incoming",
        responseType: ResponseType.Chain, priority: 100, graphId: GraphIds.Reflect);

    // Act
    world.SubmitOrder(playerA, OrderType.CastAbility, playerB, abilityId: "Ability.Fireball");
    world.Tick(5);  // Proposal → Chain → Resolve

    // Assert
    // 最终 A 受到伤害（反射回自己）
    Assert.Less(GetAttribute(world, playerA, "Health"), 500f);
    Assert.AreEqual(500f, GetAttribute(world, playerB, "Health")); // B 不受伤
}

[Test]
public void O2_ChainResponse_HookCancels_NoEffect()
{
    // B 有 Hook → 火球被取消
    // Assert: B HP = 500 (不变)
}

[Test]
public void O2_ChainResponse_MultipleModify_StackCorrectly()
{
    // B 有护甲（priority=50）和护盾（priority=100）
    // Assert: FinalDamage = 100 * 0.7 - 20 = 50
}

[Test]
public void O2_ChainResponse_MaxDepth_PreventInfiniteLoop()
{
    // 配置循环响应（A 响应 B，B 响应 A）
    // Assert: 达到最大深度后自动 Pass
}
```

### 集成验收
1. 运行 `dotnet test --filter "O2_ChainResponse"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o2_chain_response_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o2_double_reflect.png`
   - `artifacts/acceptance/response_window/o2_hook_cancel.png`
   - `artifacts/acceptance/response_window/o2_multiple_modify.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o2_60frames.mp4`
   - 标注关键帧：提议帧、响应帧、结算帧

---

## 参考案例

- **游戏王 连锁**: 玩家 A 发动魔法 → 玩家 B 发动陷阱 → 玩家 A 再发动反制陷阱；逆序结算。
- **万智牌 堆栈**: 瞬间牌可以响应其他瞬间牌，形成堆栈；后进先出结算。
- **Dota Lotus Orb**: 反射单体指向技能，可形成多层反射（如双方都有 Lotus Orb）。
