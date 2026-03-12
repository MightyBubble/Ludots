# S1: 组合键（Combo Key）

> 按住修饰键（Shift/Alt/Ctrl）+ 主动作键触发变体技能。如：战神 L1+R1 触发符文攻击。

---

## 机制描述

玩家按住修饰键（如 Shift、Alt、Ctrl）的同时按下主动作键，触发与单独按键不同的技能或行为。常见于：
- **战神**：L1+R1 触发符文攻击（轻/重）
- **只狼**：Shift+攻击 = 垫步攻击
- **MOBA**：Shift+技能 = 队列施放

与 S4（Alt 自我施放）的区别：S1 是完全不同的技能 ID，S4 是同一技能的目标覆盖。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 根据技能需求（`Entity` / `Position` / `None`）
- **InteractionMode**: 根据技能需求（`TargetFirst` / `SmartCast`）

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_Q_shift.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q_shift",                  // 独立的 ActionId（在 InputBackend 中配为 Shift+Q）
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "modifierSubmitBehavior": "Normal",             // 不需要特殊 modifier 行为（组合键已在 InputBackend 处理）
  "autoTargetPolicy": "NearestEnemyInRange",
  "autoTargetRangeCm": 600
}
```

**关键点**：
- 组合键由 `PlayerInputHandler` 的 `CompositeBinding` 配置处理（Unity Input System 层）
- 在 `InputOrderMapping` 中注册为独立的 `actionId`（如 `ability_Q_shift`）
- 不需要在 Order 层做修饰键判断，Input 层已完成组合键识别

---

## Graph 实现

组合键技能的 Graph 实现与普通技能完全相同，差异仅在 `actionId` 和技能参数配置。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/ability_Q_shift_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标
  LoadAttribute             E[0], BaseDamage → F[0]
  LoadConfigFloat           "DamageCoeff" → F[1]  // 组合键技能通常有更高系数
  MulFloat                  F[0], F[1]   → F[2]
  WriteBlackboardFloat      E[effect], DamageAmount, F[2]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[0]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Q_Shift.Main",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 2.0                    // 组合键技能伤害系数更高
  },
  "grantedTags": ["Ability.Q_Shift.Active"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| PlayerInputHandler | `src/Core/Input/PlayerInputHandler.cs` | ✅ 已有（CompositeBinding 支持） |
| InputOrderMappingSystem | `src/Core/Input/Orders/InputOrderMappingSystem.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 无 | - | 现有基建可完整表达。组合键识别由 Unity Input System 的 CompositeBinding 处理，Order 层只需注册独立 actionId。 |

---

## 最佳实践

- **DO**: 为组合键技能创建独立的 `actionId`（如 `ability_Q_shift`），在 InputBackend 配置中绑定为 Shift+Q。
- **DO**: 组合键技能与普通技能共享相同的 Order 处理流程，无需特殊逻辑。
- **DO**: 使用 `ModifierSubmitBehavior=Normal`，修饰键已在 Input 层消费，不需要传递到 Order 层。
- **DON'T**: 不要在 `InputOrderMappingSystem` 中手动检测修饰键状态（Input 层已处理）。
- **DON'T**: 不要将组合键技能与 S7（Shift 队列）混淆：S7 是 `ModifierSubmitBehavior=QueueOnModifier`，S1 是独立技能 ID。

---

## 验收口径

### 场景 1: 正常组合键施放

| 项 | 内容 |
|----|------|
| **输入** | 玩家按住 Shift，按下 Q 键（`PressedThisFrame`），悬停目标：敌方单位 HP=500 |
| **预期输出** | 触发 `ability_Q_shift` 技能，伤害系数 2.0，目标 HP 扣减 400（假设 BaseDamage=200） |
| **Log 关键字** | `[Input] OrderSubmitted actionId=ability_Q_shift target=<tid>` <br> `[GAS] ApplyEffect Effect.Ability.Q_Shift.Main -> source=<sid> target=<tid> FinalDamage=400` |
| **截图要求** | 伤害浮字显示 400，技能特效与普通 Q 不同（如颜色/粒子） |
| **多帧录屏** | F0: 按 Shift+Q → F1: Order 入队 → F2: Effect Propose → F4: OnApply → F5: HP 变化 → F6: 浮字出现 |

### 场景 2: 仅按主键（无修饰键）

| 项 | 内容 |
|----|------|
| **输入** | 玩家仅按 Q 键（不按 Shift） |
| **预期输出** | 触发 `ability_Q` 技能（普通版本），伤害系数 1.0 |
| **Log 关键字** | `[Input] OrderSubmitted actionId=ability_Q target=<tid>` <br> `[GAS] ApplyEffect Effect.Ability.Q.Main -> FinalDamage=200` |
| **截图要求** | 伤害浮字显示 200，普通技能特效 |
| **多帧录屏** | 与场景 1 对比，确认技能 ID 和效果不同 |

### 场景 3: 组合键 + 资源不足

| 项 | 内容 |
|----|------|
| **输入** | 按 Shift+Q，但 Mana < Cost（组合键技能消耗更高） |
| **预期输出** | 激活失败；UI 提示法力不足 |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=ability_Q_shift reason=InsufficientMana` |
| **截图要求** | 技能图标闪烁红色，无施放动作 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/ComboKeyTests.cs
[Test]
public void S1_ComboKey_TriggersVariantAbility()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 100);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_shift");
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 2.0 = 400
    Assert.AreEqual(100, GetAttribute(world, target, "Health"));
    Assert.IsTrue(HasTag(world, source, "Ability.Q_Shift.Active"));
}

[Test]
public void S1_NormalKey_TriggersNormalAbility()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 1.0 = 200
    Assert.AreEqual(300, GetAttribute(world, target, "Health"));
    Assert.IsFalse(HasTag(world, source, "Ability.Q_Shift.Active"));
}

[Test]
public void S1_ComboKey_InsufficientMana_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 10);  // Cost = 50
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_shift");
    world.Tick(3);

    // Assert
    Assert.AreEqual(500, GetAttribute(world, target, "Health"));  // 未扣血
    Assert.AreEqual(10, GetAttribute(world, source, "Mana"));     // 未消耗
}
```

### 集成验收
1. 运行 `dotnet test --filter "S1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s1_combo_key_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s1_normal.png`（普通 Q）
   - `artifacts/acceptance/special_input/s1_combo.png`（Shift+Q）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s1_60frames.gif`
   - 标注关键帧：按键帧、Effect Apply 帧、结果帧

---

## 参考案例

- **战神（God of War）符文攻击**: L1+R1 触发符文轻攻击，L1+R2 触发符文重攻击，独立冷却和资源消耗。
- **只狼（Sekiro）垫步攻击**: Shift+攻击 = 快速突进攻击，伤害和动作与普通攻击不同。
- **英雄联盟（LoL）**: Shift+技能 = 队列施放（但这是 S7 机制，不是 S1）。
