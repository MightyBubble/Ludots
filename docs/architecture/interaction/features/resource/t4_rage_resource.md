# T4: 怒气资源（Rage Resource）

> 怒气值通过战斗积累，达到阈值后可以释放怒气技能。如：WoW 战士怒气系统。

---

## 机制描述

怒气是战斗驱动的资源，通过造成/承受伤害积累，离战后逐渐衰减。怒气达到阈值后可释放强力技能。常见于：
- **魔兽世界（WoW）**：战士怒气系统
- **战神（God of War）**：怒气技能
- **暗黑破坏神 3（Diablo 3）**：野蛮人狂暴值

与 T1（法力）的区别：T1 自然回复，T4 战斗积累+离战衰减。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 技能决定
- **InteractionMode**: 技能决定

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/rage_ability.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "rage_ability",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/rage_ability_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// 门控阶段：检查怒气 >= 阈值
Phase OnPropose Gate:
  LoadContextSource         E[0]          // 施法者
  LoadAttribute             E[0], Rage   → F[0]
  LoadConfigFloat           "RageCost"   → F[1]
  CompareGtFloat            F[0], F[1]   → B[0]  // Rage >= RageCost?
  JumpIfFalse               B[0], FAIL_LABEL

// 执行阶段：消耗怒气 + 技能效果
Phase OnApply Main:
  LoadContextSource         E[0]
  LoadContextTarget         E[1]
  // 消耗怒气
  LoadConfigFloat           "RageCost"   → F[0]
  NegFloat                  F[0]         → F[1]
  ModifyAttributeAdd        E[effect], E[0], Rage, F[1]
  // 技能主效果（高伤害）
  LoadAttribute             E[0], BaseDamage → F[2]
  LoadConfigFloat           "DamageCoeff"   → F[3]  // 怒气技能系数高
  MulFloat                  F[2], F[3]      → F[4]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[4]
```

怒气积累 Effect（OnDamageDealt Listener）：
```json5
{
  "id": "Effect.Passive.RageGeneration",
  "presetType": "Buff",
  "lifetime": { "kind": "Permanent" },
  "phaseListeners": [
    {
      "phase": "OnDamageDealt",
      "scope": "Source",
      "graph": [
        // 造成伤害时积累怒气
        { "op": "ReadBlackboardFloat", "key": "FinalDamage", "F": 0 },
        { "op": "LoadConfigFloat", "key": "RageGainFactor", "F": 1 },  // 0.1 = 10% 伤害转怒气
        { "op": "MulFloat", "a": "F[0]", "b": "F[1]", "F": 2 },
        { "op": "LoadContextSource", "E": 0 },
        { "op": "ModifyAttributeAdd", "entity": "E[0]", "attr": "Rage", "delta": "F[2]" }
      ]
    }
  ]
}
```

怒气衰减 Effect（离战后）：
```json5
{
  "id": "Effect.Passive.RageDecay",
  "presetType": "Buff",
  "lifetime": { "kind": "Permanent" },
  "phaseListeners": [
    {
      "phase": "OnPeriod",
      "periodTicks": 60,  // 每秒检查一次
      "graph": [
        // 若持有 OutOfCombat Tag，衰减怒气
        { "op": "LoadContextSource", "E": 0 },
        { "op": "HasTag", "entity": "E[0]", "tag": "Status.OutOfCombat", "B": 0 },
        { "op": "JumpIfFalse", "cond": "B[0]", "label": "DONE" },
        { "op": "ConstFloat", "value": -5.0, "F": 0 },  // 每秒 -5 怒气
        { "op": "ModifyAttributeAdd", "entity": "E[0]", "attr": "Rage", "delta": "F[0]" }
      ]
    }
  ]
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| OnDamageDealt Listener | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有（Phase Listener 机制） |
| GraphOps.ReadBlackboardFloat (300) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.HasTag (33) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AbilityActivationBlockTags.RequiredAll | `src/Core/Gameplay/GAS/AbilitySpec.cs` | ✅ 已有 — 对应 RequiredAll 字段；Attribute Precondition 仍为 P1 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P1 | 技能激活时需检查 `Rage >= RageCost`，复用 T1/G8 需求。 |

---

## 最佳实践

- **DO**: 怒气积累通过 `OnDamageDealt` Phase Listener 实现，不要在 System 层硬编码。
- **DO**: 怒气衰减通过 `OnPeriod` + `OutOfCombat` Tag 条件判断，离战后自动触发。
- **DO**: 怒气上限通常为 100，通过 `MaxRage` Attribute 配置。
- **DON'T**: 不要将怒气与法力混淆：怒气不自然回复，只在战斗中积累。
- **DON'T**: 不要在 Graph 外硬编码怒气积累公式，统一走 `RageGainFactor` 配置。

---

## 验收口径

### 场景 1: 造成伤害积累怒气

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Rage=0，造成 200 伤害（RageGainFactor=0.1） |
| **预期输出** | Rage 增加 20（200 * 0.1） |
| **Log 关键字** | `[GAS] OnDamageDealt FinalDamage=200 -> RageGain=20 Rage: 0→20` |
| **截图要求** | 怒气条上升，显示 20/100 |
| **多帧录屏** | F0: 攻击 → F1: 伤害结算 → F2: 怒气增加 |

### 场景 2: 怒气不足无法施放

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Rage=30，RageCost=50 |
| **预期输出** | 激活失败，UI 提示"怒气不足" |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=rage_ability reason=InsufficientRage Rage=30 Cost=50` |
| **截图要求** | 技能图标闪烁红色×，无施放动作 |

### 场景 3: 离战后怒气衰减

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Rage=50，持有 `OutOfCombat` Tag，等待 60 ticks |
| **预期输出** | Rage 减少 5（每秒 -5） |
| **Log 关键字** | `[GAS] OnPeriod RageDecay -> Rage: 50→45` |
| **截图要求** | 怒气条缓慢下降 |
| **多帧录屏** | F0: 离战 → F60: 怒气 -5 → F120: 怒气 -5 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/Resource/RageResourceTests.cs
[Test]
public void T4_DealDamage_GeneratesRage()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, rage: 0, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "basic_attack");
    world.Tick(3);

    // Assert
    // FinalDamage = 200, RageGain = 200 * 0.1 = 20
    Assert.AreEqual(20, GetAttribute(world, source, "Rage"));
}

[Test]
public void T4_InsufficientRage_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, rage: 30);  // Cost = 50
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "rage_ability");
    world.Tick(3);

    // Assert
    Assert.AreEqual(30, GetAttribute(world, source, "Rage"));  // 未消耗
    Assert.AreEqual(500, GetAttribute(world, target, "Health"));  // 未扣血
}

[Test]
public void T4_OutOfCombat_RageDecays()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, rage: 50);
    ApplyTag(world, source, "Status.OutOfCombat");

    // Act
    world.Tick(60);  // 1 秒

    // Assert
    Assert.AreEqual(45, GetAttribute(world, source, "Rage"));  // 50 - 5 = 45
}
```

### 集成验收
1. 运行 `dotnet test --filter "T4"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/resource/t4_rage_resource_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/resource/t4_gain.png`（怒气积累）
   - `artifacts/acceptance/resource/t4_decay.png`（怒气衰减）
4. 多帧录屏：
   - `artifacts/acceptance/resource/t4_60frames.gif`
   - 标注关键帧：伤害帧、怒气增加帧、离战帧、怒气衰减帧

---

## 参考案例

- **魔兽世界（WoW）战士怒气**: 造成/承受伤害积累怒气，离战后快速衰减，怒气技能伤害高但消耗大。
- **战神（God of War）怒气技能**: 战斗中积累怒气条，满怒气后可释放强力技能，提供爆发输出窗口。
- **暗黑破坏神 3（Diablo 3）野蛮人狂暴值**: 类似怒气机制，战斗中积累，离战后衰减，驱动核心技能。
