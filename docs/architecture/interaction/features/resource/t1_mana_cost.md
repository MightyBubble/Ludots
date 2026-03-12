# T1: 法力消耗（Mana Cost）

> 技能释放需要消耗蓝量（Mana），不足时无法释放。如：LoL 绝大多数技能的蓝量门控。

---

## 机制描述

法力是最常见的技能资源，施法者需要持有足够的法力值才能释放技能。法力不足时技能激活失败，并给予视觉/音效反馈。常见于：
- **英雄联盟（LoL）**：绝大多数技能有蓝量消耗
- **Dota 2**：法力点消耗
- **魔兽世界（WoW）**：法力值（Mana）系统

与 T5（生命消耗）的区别：T1 消耗蓝量（Mana），T5 消耗生命值（HP），前者通常无死亡风险，后者有。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 技能决定
- **InteractionMode**: 技能决定

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_Q.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "castModeOverride": null
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/ability_Q_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// 门控阶段（OnPropose 或 AbilityActivationCheck）:
Phase OnPropose Gate:
  LoadContextSource         E[0]          // 施法者
  LoadAttribute             E[0], Mana  → F[0]
  LoadConfigFloat           "ManaCost"  → F[1]
  CompareGtFloat            F[0], F[1]  → B[0]  // Mana > ManaCost?
  JumpIfFalse               B[0], FAIL_LABEL     // 不足 → 失败

// 执行阶段（OnApply）:
Phase OnApply Main:
  LoadContextSource         E[0]
  LoadContextTarget         E[1]
  // 先扣蓝
  LoadConfigFloat           "ManaCost"  → F[1]
  NegFloat                  F[1]        → F[2]
  ModifyAttributeAdd        E[effect], E[0], Mana, F[2]
  // 再结算效果
  LoadAttribute             E[0], BaseDamage → F[3]
  LoadConfigFloat           "DamageCoeff"   → F[4]
  MulFloat                  F[3], F[4]      → F[5]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[5]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Q.Main",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "ManaCost": 50.0,
    "DamageCoeff": 1.5
  },
  "grantedTags": ["Ability.Q.Active"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| AttributeAggregatorSystem | `src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs` | ✅ 已有 |
| GraphOps.LoadAttribute (10) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.CompareGtFloat (30) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.JumpIfFalse (7) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.ModifyAttributeAdd (210) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AbilityActivationBlockTags.RequiredAll | `src/Core/Gameplay/GAS/AbilitySpec.cs` | ✅ 已有 — 对应 RequiredAll 字段；Attribute Precondition 仍为 P1 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P1 | AbilitySpec 需支持在激活检查阶段（OnPropose 前）对 Attribute 做门控，失败时返回 `ActivationFailed.InsufficientResource`，复用 G8 需求。 |

---

## 最佳实践

- **DO**: 使用 `OnPropose` 阶段做蓝量门控，确保技能在资源不足时不进入 `OnApply` 阶段。
- **DO**: 蓝量消耗在 `OnApply Main` 中执行，与技能效果原子性绑定（要么都发生要么都不发生）。
- **DO**: 蓝量自然回复通过 `OnPeriod` HoT 型 Effect 实现（类似血量回复），不要在 System 层写死。
- **DON'T**: 不要在 Graph 外硬编码蓝量消耗数值，统一走 `configParams.ManaCost`。
- **DON'T**: 不要在 `OnApply` 前消耗蓝量（避免效果失败但已扣蓝）。

---

## 验收口径

### 场景 1: 蓝量充足，正常施放

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 Q 键，施法者 Mana=100，ManaCost=50，目标 HP=500 |
| **预期输出** | 技能正常施放，Mana 变为 50，目标 HP 扣减 300 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.Q.Main -> ManaCost=50 Mana: 100→50` <br> `[GAS] ApplyEffect FinalDamage=300 Health: 500→200` |
| **截图要求** | 蓝条扣减 50，目标受到伤害浮字 300 |
| **多帧录屏** | F0: 按键 → F1: Gate 检查通过 → F2: Effect Apply → F3: Mana 扣减 → F4: HP 扣减 → F5: 浮字 |

### 场景 2: 蓝量不足，激活失败

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Mana=30，ManaCost=50 |
| **预期输出** | 激活失败；UI 提示"蓝量不足"；Mana 和目标 HP 均无变化 |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=ability_Q reason=InsufficientMana Mana=30 Cost=50` |
| **截图要求** | 技能图标闪烁蓝色×，无施放动画 |
| **多帧录屏** | F0: 按键 → F1: Gate 检查失败 → 后续帧静止 |

### 场景 3: 蓝量恰好等于费用

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Mana=50，ManaCost=50 |
| **预期输出** | 技能正常施放（≥ cost 通过），Mana 变为 0 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.Q.Main -> Mana: 50→0` |
| **截图要求** | 蓝条清零，技能正常施放 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/Resource/ManaCostTests.cs
[Test]
public void T1_SufficientMana_AbilityFires()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 100);
    var target = SpawnUnit(world, hp: 500, armor: 0);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    // FinalDamage = 200 * 1.5 = 300
    Assert.AreEqual(200, GetAttribute(world, target, "Health"));
    Assert.AreEqual(50, GetAttribute(world, source, "Mana"));   // 100 - 50 = 50
}

[Test]
public void T1_InsufficientMana_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 30);  // Cost = 50
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    Assert.AreEqual(500, GetAttribute(world, target, "Health"));  // 未扣血
    Assert.AreEqual(30, GetAttribute(world, source, "Mana"));     // 未消耗
}

[Test]
public void T1_ExactManaCost_SucceedsAndEmptiesMana()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 50);  // Cost = 50
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    Assert.AreEqual(200, GetAttribute(world, target, "Health"));  // 已扣血
    Assert.AreEqual(0, GetAttribute(world, source, "Mana"));      // 蓝量清零
}
```

### 集成验收
1. 运行 `dotnet test --filter "T1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/resource/t1_mana_cost_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/resource/t1_normal.png`（蓝量充足）
   - `artifacts/acceptance/resource/t1_insufficient.png`（蓝量不足）
4. 多帧录屏：
   - `artifacts/acceptance/resource/t1_60frames.gif`
   - 标注关键帧：按键帧、Gate 检查帧、Mana 扣减帧、HP 扣减帧

---

## 参考案例

- **英雄联盟（LoL）蓝量系统**: 绝大多数技能消耗蓝量，蓝量自然回复，出装可提升蓝量上限和回复速度。
- **Dota 2 法力点**: 法力消耗类似，但部分英雄完全没有蓝量（靠其他资源）。
- **魔兽世界（WoW）法力值**: 各职业的主资源，休息/喝水可快速回复。
