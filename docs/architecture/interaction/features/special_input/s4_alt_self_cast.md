# S4: Alt 自我施放（Alt Self Cast）

> 按住 Alt 键释放目标型技能时，强制以自身为目标进行施放。如：Dota 2 Alt+治疗 = 自我治疗。

---

## 机制描述

玩家按住 Alt 键的同时释放需要目标的技能，技能自动以施法者自身为目标，无需手动选中自己。常见于：
- **Dota 2**：Alt+治疗 = 自我治疗
- **英雄联盟（LoL）**：Alt+护盾 = 自我护盾
- **风暴英雄（HotS）**：Alt+增益技能 = 自我增益

与 S1（组合键）的区别：S4 是同一技能的目标覆盖，S1 是完全不同的技能 ID。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Entity`（但强制 target = self）
- **InteractionMode**: `TargetFirst` / `SmartCast`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/heal_spell.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "heal_spell",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "castModeOverride": "TargetFirst",
  "autoTargetPolicy": "None",                     // Alt 按下时不自动选目标
  "modifierBehavior": {
    "AltHeld": {
      "forceTargetSelf": true                     // Alt 按下时强制目标 = self
    }
  }
}
```

**关键点**：
- `ModifierBehavior.AltHeld.forceTargetSelf = true` 覆盖目标选择逻辑
- 在 `InputOrderMappingSystem` 中检测 Alt 键状态，将 `Order.Target` 设为施法者自身

---

## Graph 实现

Alt 自我施放的 Graph 实现与普通目标施放完全相同，差异仅在目标选择。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/heal_spell_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标（Alt 按下时 = E[0]）
  LoadAttribute             E[0], HealPower → F[0]
  LoadConfigFloat           "HealCoeff" → F[1]
  MulFloat                  F[0], F[1]   → F[2]
  WriteBlackboardFloat      E[effect], HealAmount, F[2]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], HealAmount → F[0]
  ModifyAttributeAdd        E[effect], E[1], Health, F[0]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.HealSpell.Main",
  "presetType": "Heal",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "HealCoeff": 1.5
  },
  "grantedTags": ["Ability.HealSpell.Active"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| InputOrderMappingSystem | `src/Core/Input/Orders/InputOrderMappingSystem.cs` | ❌ P2 — 需支持 ModifierBehavior.forceTargetSelf |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ModifierBehavior.forceTargetSelf | P2 | `InputOrderMappingSystem` 需检测 Alt 键状态，当 `forceTargetSelf = true` 时，将 `Order.Target` 设为施法者自身（`Order.Actor`）。 |

**实现思路**：
```csharp
// src/Core/Input/Orders/InputOrderMappingSystem.cs
public void ProcessInput(InputOrderMapping mapping)
{
    if (mapping.ModifierBehavior?.AltHeld?.ForceTargetSelf == true && IsAltHeld())
    {
        var order = new Order
        {
            Actor = localPlayerEntity,
            Target = localPlayerEntity,  // 强制目标 = 自身
            OrderTypeKey = mapping.OrderTypeKey,
            Args = mapping.ArgsTemplate
        };
        SubmitOrder(order);
        return;
    }

    // 正常目标选择逻辑
    // ...
}
```

---

## 最佳实践

- **DO**: 为自我施放提供视觉反馈（如技能图标边框变色），提示玩家 Alt 键已生效。
- **DO**: 自我施放与普通施放共享相同的冷却和资源消耗。
- **DO**: 在 UI 中显示 Alt 键提示（如技能图标下方显示"Alt: 自我施放"）。
- **DON'T**: 不要在 Graph 层实现目标覆盖（属于 Input 层职责）。
- **DON'T**: 不要将 Alt 自我施放与 SmartCast 的自动 fallback 混淆：Alt 是明确的强制行为，SmartCast fallback 是无目标时的兜底。

---

## 验收口径

### 场景 1: Alt + 技能（自我施放）

| 项 | 内容 |
|----|------|
| **输入** | 玩家按住 Alt，按下治疗技能键，当前 HP=300/500 |
| **预期输出** | 治疗施法者自身，HP 恢复到 450/500（假设 HealAmount=150） |
| **Log 关键字** | `[Input] AltHeld detected, forceTargetSelf=true` <br> `[Input] OrderSubmitted actionId=heal_spell target=<self_id>` <br> `[GAS] ApplyEffect Effect.Ability.HealSpell.Main -> source=<self_id> target=<self_id> HealAmount=150` |
| **截图要求** | 治疗特效出现在施法者身上，HP 条上升，浮字显示 +150 |
| **多帧录屏** | F0: 按 Alt+技能键 → F1: Order 入队 → F2: Effect Propose → F4: OnApply → F5: HP 变化 → F6: 浮字出现 |

### 场景 2: 无 Alt（正常目标施放）

| 项 | 内容 |
|----|------|
| **输入** | 玩家不按 Alt，按下治疗技能键，悬停目标：友方单位 HP=200/500 |
| **预期输出** | 治疗目标友方单位，HP 恢复到 350/500 |
| **Log 关键字** | `[Input] OrderSubmitted actionId=heal_spell target=<ally_id>` <br> `[GAS] ApplyEffect Effect.Ability.HealSpell.Main -> source=<self_id> target=<ally_id> HealAmount=150` |
| **截图要求** | 治疗特效出现在友方单位身上，HP 条上升 |
| **多帧录屏** | 与场景 1 对比，确认目标不同 |

### 场景 3: Alt + 技能（资源不足）

| 项 | 内容 |
|----|------|
| **输入** | 按 Alt+治疗键，但 Mana < Cost |
| **预期输出** | 激活失败；UI 提示法力不足 |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=heal_spell reason=InsufficientMana` |
| **截图要求** | 技能图标闪烁红色，无施放动作 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/AltSelfCastTests.cs
[Test]
public void S4_AltHeld_ForcesSelfTarget()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 300, maxHp: 500, healPower: 100, mana: 100);
    var ally = SpawnUnit(world, hp: 200, maxHp: 500);

    // Act
    world.HoldKey(source, "Alt");
    world.SubmitOrder(source, OrderType.CastAbility, ally, abilityId: "heal_spell");  // 悬停 ally
    world.Tick(3);

    // Assert
    // HealAmount = 100 * 1.5 = 150
    Assert.AreEqual(450, GetAttribute(world, source, "Health"));  // 自身被治疗
    Assert.AreEqual(200, GetAttribute(world, ally, "Health"));    // ally 未被治疗
}

[Test]
public void S4_NoAlt_TargetsAlly()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 300, maxHp: 500, healPower: 100, mana: 100);
    var ally = SpawnUnit(world, hp: 200, maxHp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, ally, abilityId: "heal_spell");
    world.Tick(3);

    // Assert
    Assert.AreEqual(300, GetAttribute(world, source, "Health"));  // 自身未被治疗
    Assert.AreEqual(350, GetAttribute(world, ally, "Health"));    // ally 被治疗
}

[Test]
public void S4_AltHeld_InsufficientMana_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 300, maxHp: 500, mana: 10);  // Cost = 50

    // Act
    world.HoldKey(source, "Alt");
    world.SubmitOrder(source, OrderType.CastAbility, source, abilityId: "heal_spell");
    world.Tick(3);

    // Assert
    Assert.AreEqual(300, GetAttribute(world, source, "Health"));  // 未治疗
    Assert.AreEqual(10, GetAttribute(world, source, "Mana"));     // 未消耗
}
```

### 集成验收
1. 运行 `dotnet test --filter "S4"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s4_alt_self_cast_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s4_self.png`（Alt+治疗）
   - `artifacts/acceptance/special_input/s4_ally.png`（普通治疗）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s4_60frames.gif`
   - 标注关键帧：按键帧、目标选择、治疗触发、HP 变化

---

## 参考案例

- **Dota 2 自我施放**: Alt+治疗/护盾技能 = 自我施放，无需手动选中自己。
- **英雄联盟（LoL）自我施放**: Alt+技能 = 强制自我施放，常用于紧急自保。
- **风暴英雄（HotS）自我施放**: Alt+增益技能 = 自我增益，提升操作效率。
