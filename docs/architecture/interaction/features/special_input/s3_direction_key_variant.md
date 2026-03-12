# S3: 方向键变体（Direction Key Variant）

> 通过方向键 + 技能键的组合触发方向变体技能。如：铁拳前进+攻击 = 上勾拳，后退+攻击 = 后撤步。

---

## 机制描述

玩家按住方向键的同时按下技能键，根据方向输入触发不同的技能变体。常见于：
- **铁拳（Tekken）**：前进+攻击 = 上勾拳，后退+攻击 = 后撤步
- **街霸（Street Fighter）**：前进+重拳 = 冲拳，下+重拳 = 下勾拳
- **任天堂明星大乱斗（Smash Bros）**：方向键决定使用哪个 B 技能

与 S1（组合键）的区别：S3 的方向是连续输入（摇杆/WASD），S1 是离散按键。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Direction`（方向输入）
- **InteractionMode**: `ContextScored`（根据方向 dot 评分）

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/attack_directional.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "attack_directional",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Direction",                   // 方向作为选择输入
  "isSkillMapping": true,
  "contextGroupId": "DirectionalAttackGroup"      // 使用 ContextGroup 评分
}
```

**ContextGroup 配置**：
```json5
// 配置路径: mods/<yourMod>/ContextGroups/DirectionalAttackGroup.json
{
  "id": "DirectionalAttackGroup",
  "candidates": [
    {
      "id": "ForwardAttack",
      "precondition": "direction.dot(forward) > 0.7",
      "argsTemplate": { "abilityId": "forward_slash" },
      "scoreFormula": "direction.dot(forward) * 100"
    },
    {
      "id": "BackAttack",
      "precondition": "direction.dot(backward) > 0.7",
      "argsTemplate": { "abilityId": "back_kick" },
      "scoreFormula": "direction.dot(backward) * 100"
    },
    {
      "id": "SideAttack",
      "precondition": "abs(direction.dot(right)) > 0.7",
      "argsTemplate": { "abilityId": "side_step" },
      "scoreFormula": "abs(direction.dot(right)) * 100"
    }
  ]
}
```

---

## Graph 实现

每个方向变体技能有独立的 Effect 模板，Graph 实现根据技能类型不同。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/forward_slash_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate (forward_slash):
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标
  LoadAttribute             E[0], BaseDamage → F[0]
  LoadConfigFloat           "DamageCoeff" → F[1]  // 前进攻击系数 1.5
  MulFloat                  F[0], F[1]   → F[2]
  WriteBlackboardFloat      E[effect], DamageAmount, F[2]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[0]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.ForwardSlash.Main",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 1.5                    // 前进攻击伤害系数
  },
  "grantedTags": ["Ability.ForwardSlash.Active"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| ContextGroup | `src/Core/Input/Context/ContextGroup.cs` | ❌ P1 — 需新增 ContextGroup 评分机制 |
| SelectionSystem | `src/Core/Input/Selection/SelectionSystem.cs` | ❌ P2 — 需支持 Direction selectionType |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ContextGroup 评分机制 | P1 | 需实现 ContextGroup 注册、评分器调用、候选项排序。参考 N 系列 context_scored 需求。 |
| Direction selectionType | P2 | `OrderSelectionType.Direction` 需支持方向输入（摇杆/WASD）作为选择参数，传递到 Order.Args.Direction。 |

**实现思路**：
```csharp
// src/Core/Input/Context/ContextGroup.cs
public class ContextGroup
{
    public string Id { get; set; }
    public List<ContextCandidate> Candidates { get; set; }
}

public class ContextCandidate
{
    public string Id { get; set; }
    public string Precondition { get; set; }        // Graph 表达式
    public OrderArgsTemplate ArgsTemplate { get; set; }
    public string ScoreFormula { get; set; }        // Graph 表达式
}

// src/Core/Input/Context/ContextScoringSystem.cs
public class ContextScoringSystem
{
    public OrderArgs EvaluateBestCandidate(ContextGroup group, Vector2 direction)
    {
        var validCandidates = group.Candidates
            .Where(c => EvaluatePrecondition(c.Precondition, direction))
            .Select(c => (candidate: c, score: EvaluateScore(c.ScoreFormula, direction)))
            .OrderByDescending(x => x.score)
            .ToList();

        return validCandidates.FirstOrDefault().candidate?.ArgsTemplate;
    }
}
```

---

## 最佳实践

- **DO**: 使用 ContextGroup 评分机制，避免硬编码方向判断逻辑。
- **DO**: 为每个方向变体设置合理的 dot 阈值（如 0.7），避免误触发。
- **DO**: 提供视觉反馈（如方向指示器），帮助玩家理解当前方向会触发哪个技能。
- **DON'T**: 不要在 Graph 层实现方向判断（属于 Input 层职责）。
- **DON'T**: 不要将方向变体与 S1（组合键）混淆：S3 是连续方向输入，S1 是离散按键组合。

---

## 验收口径

### 场景 1: 前进攻击

| 项 | 内容 |
|----|------|
| **输入** | 玩家按住前进键（direction.dot(forward) = 0.9），按下攻击键 |
| **预期输出** | 触发 `forward_slash` 技能，伤害系数 1.5 |
| **Log 关键字** | `[Input] DirectionDetected direction=(0, 1) dot=0.9` <br> `[Context] BestCandidate id=ForwardAttack score=90` <br> `[GAS] ApplyEffect Effect.Ability.ForwardSlash.Main -> FinalDamage=300` |
| **截图要求** | 角色向前挥砍，伤害浮字显示 300 |
| **多帧录屏** | F0: 按住前进键 → F1: 按下攻击键 → F2: 方向评分 → F3: 触发前进攻击 → F5: 伤害结算 |

### 场景 2: 后退攻击

| 项 | 内容 |
|----|------|
| **输入** | 玩家按住后退键（direction.dot(backward) = 0.85），按下攻击键 |
| **预期输出** | 触发 `back_kick` 技能，伤害系数 1.2 |
| **Log 关键字** | `[Context] BestCandidate id=BackAttack score=85` <br> `[GAS] ApplyEffect Effect.Ability.BackKick.Main -> FinalDamage=240` |
| **截图要求** | 角色向后踢击，伤害浮字显示 240 |
| **多帧录屏** | 与场景 1 对比，确认技能动作和伤害不同 |

### 场景 3: 无方向输入（默认攻击）

| 项 | 内容 |
|----|------|
| **输入** | 玩家不按方向键，直接按下攻击键 |
| **预期输出** | 触发默认攻击（如 `neutral_attack`），或无候选项通过 precondition |
| **Log 关键字** | `[Context] NoCandidateMatched group=DirectionalAttackGroup` <br> `[Input] FallbackToDefault abilityId=neutral_attack` |
| **截图要求** | 角色原地攻击，伤害浮字显示默认值 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/DirectionKeyVariantTests.cs
[Test]
public void S3_ForwardDirection_TriggersForwardSlash()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);
    var direction = new Vector2(0, 1);  // 前进方向

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, direction: direction);
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 1.5 = 300
    Assert.AreEqual(200, GetAttribute(world, target, "Health"));
    Assert.IsTrue(HasTag(world, source, "Ability.ForwardSlash.Active"));
}

[Test]
public void S3_BackwardDirection_TriggersBackKick()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);
    var direction = new Vector2(0, -1);  // 后退方向

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, direction: direction);
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 1.2 = 240
    Assert.AreEqual(260, GetAttribute(world, target, "Health"));
    Assert.IsTrue(HasTag(world, source, "Ability.BackKick.Active"));
}

[Test]
public void S3_NoDirection_FallbackToDefault()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);
    var direction = Vector2.Zero;  // 无方向输入

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, direction: direction);
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 1.0 = 200（默认攻击）
    Assert.AreEqual(300, GetAttribute(world, target, "Health"));
    Assert.IsTrue(HasTag(world, source, "Ability.NeutralAttack.Active"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "S3"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s3_direction_key_variant_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s3_forward.png`（前进攻击）
   - `artifacts/acceptance/special_input/s3_backward.png`（后退攻击）
   - `artifacts/acceptance/special_input/s3_neutral.png`（无方向）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s3_60frames.gif`
   - 标注关键帧：方向输入、攻击键按下、技能触发、伤害结算

---

## 参考案例

- **铁拳（Tekken）方向攻击**: 前进+攻击 = 上勾拳，后退+攻击 = 后撤步，侧向+攻击 = 侧踢。
- **街霸（Street Fighter）方向重拳**: 前进+重拳 = 冲拳，下+重拳 = 下勾拳，跳跃+重拳 = 空中重拳。
- **任天堂明星大乱斗（Smash Bros）B 技能**: 上+B = 上升攻击，下+B = 下砸攻击，侧+B = 侧向冲刺。
