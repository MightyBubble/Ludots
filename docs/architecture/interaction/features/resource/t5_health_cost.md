# T5: 生命消耗（Health Cost）

> 技能释放消耗自身生命值（血量）作为代价。如：Dota 2 血魔技能消耗血量。

---

## 机制描述

技能消耗施法者自身生命值作为资源，通常伴随高风险高回报设计。可选择性地设置不致死保护（clamp to 1）。常见于：
- **Dota 2 血魔（Bloodseeker）**：技能消耗血量
- **魔兽世界（WoW）死亡骑士**：生命消耗型技能
- **流放之路（Path of Exile）**：血量消耗型技能

与 T1（法力消耗）的区别：T5 消耗生命值，有死亡风险，T1 消耗法力，无死亡风险。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 技能决定
- **InteractionMode**: 技能决定

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/blood_ability.json
{
  "actionId": "blood_ability",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true
}
```

---

## Graph 实现

```
// 门控阶段：可选的安全检查（防止自杀）
Phase OnPropose Gate:
  LoadContextSource         E[0]
  LoadAttribute             E[0], Health → F[0]
  LoadConfigFloat           "HealthCost" → F[1]
  AddFloat                  F[1], 1.0    → F[2]  // Cost + 1（确保剩余 > 1）
  CompareGtFloat            F[0], F[2]   → B[0]  // Health > Cost+1?
  JumpIfFalse               B[0], FAIL_LABEL

// 执行阶段：消耗生命 + 技能效果
Phase OnApply Main:
  LoadContextSource         E[0]
  LoadContextTarget         E[1]
  // 消耗生命
  LoadConfigFloat           "HealthCost" → F[0]
  NegFloat                  F[0]         → F[1]
  ModifyAttributeAdd        E[effect], E[0], Health, F[1]
  // 技能主效果（高伤害）
  LoadAttribute             E[0], BaseDamage → F[2]
  LoadConfigFloat           "DamageCoeff"   → F[3]  // 血量消耗技能系数高
  MulFloat                  F[2], F[3]      → F[4]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[4]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.BloodAbility.Main",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "HealthCost": 100.0,
    "DamageCoeff": 2.5  // 高伤害补偿生命消耗
  },
  "grantedTags": ["Ability.BloodAbility.Active"]
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GraphOps.ModifyAttributeAdd (210) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AbilityActivationBlockTags.RequiredAll | `src/Core/Gameplay/GAS/AbilitySpec.cs` | ✅ 已有 — 对应 RequiredAll 字段；Attribute Precondition 仍为 P1 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P1 | 技能激活时需检查 `Health > HealthCost + 1`（可选安全检查），复用 T1/G8 需求。 |
| Attribute clamp on modify | P1 | `ModifyAttributeAdd` 支持最小值 clamp（如 clamp to 1），防止自杀。 |

---

## 最佳实践

- **DO**: 为生命消耗技能设置不致死保护（clamp to 1），避免玩家意外自杀。
- **DO**: 生命消耗技能通常伴随高伤害/高收益，补偿风险。
- **DO**: 在 UI 上明确标注生命消耗数值（红色数字），提醒玩家风险。
- **DON'T**: 不要在 `OnApply` 前消耗生命（避免效果失败但已扣血）。
- **DON'T**: 不要将生命消耗与法力消耗混淆：生命消耗有死亡风险，需谨慎设计。

---

## 验收口径

### 场景 1: 生命充足，正常施放

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Health=500，HealthCost=100，目标 HP=500 |
| **预期输出** | 技能正常施放，Health 变为 400，目标 HP 扣减 500（假设 BaseDamage=200, Coeff=2.5） |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.BloodAbility.Main -> HealthCost=100 Health: 500→400` <br> `[GAS] FinalDamage=500 Health: 500→0` |
| **截图要求** | 施法者血条扣减 100，目标受到伤害浮字 500 |

### 场景 2: 生命不足（触发不致死保护）

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Health=50，HealthCost=100 |
| **预期输出** | 激活失败，UI 提示"生命不足" |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=blood_ability reason=InsufficientHealth Health=50 Cost=100` |

### 场景 3: 生命恰好等于费用+1

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Health=101，HealthCost=100 |
| **预期输出** | 技能正常施放，Health 变为 1（clamp 保护） |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.BloodAbility.Main -> Health: 101→1` |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/Resource/HealthCostTests.cs
[Test]
public void T5_SufficientHealth_AbilityFires()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 500, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);

    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "blood_ability");
    world.Tick(3);

    Assert.AreEqual(400, GetAttribute(world, source, "Health"));  // 500 - 100
    Assert.AreEqual(0, GetAttribute(world, target, "Health"));    // 500 - 500
}

[Test]
public void T5_InsufficientHealth_ActivationFailed()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 50);  // Cost = 100
    var target = SpawnUnit(world, hp: 500);

    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "blood_ability");
    world.Tick(3);

    Assert.AreEqual(50, GetAttribute(world, source, "Health"));   // 未消耗
    Assert.AreEqual(500, GetAttribute(world, target, "Health"));  // 未扣血
}

[Test]
public void T5_ClampToOne_PreventsSuicide()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 101, baseDamage: 200);  // Cost = 100
    var target = SpawnUnit(world, hp: 500);

    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "blood_ability");
    world.Tick(3);

    Assert.AreEqual(1, GetAttribute(world, source, "Health"));  // clamp to 1
}
```

### 集成验收
1. 运行 `dotnet test --filter "T5"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/resource/t5_health_cost_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/resource/t5_normal.png`（生命充足）
   - `artifacts/acceptance/resource/t5_insufficient.png`（生命不足）
4. 多帧录屏：
   - `artifacts/acceptance/resource/t5_60frames.gif`

---

## 参考案例

- **Dota 2 血魔（Bloodseeker）**: 技能消耗血量，但提供高额伤害和生命偷取，风险与收益并存。
- **魔兽世界（WoW）死亡骑士**: 部分技能消耗生命值，配合生命回复机制平衡。
- **流放之路（Path of Exile）**: 血量消耗型技能，配合能量护盾/生命回复构筑。
