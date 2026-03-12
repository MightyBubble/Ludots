# T3: 充能（Charges）

> 技能有多次充能次数，每次使用消耗一次充能，充能随时间自动回复。如：LoL 烬 Q 充能型技能。

---

## 机制描述

技能通过 Attribute（`charges`）维护可用次数，每次使用消耗 1 次，使用后启动回复计时器，计时完成后自动回复 1 次充能，直到充满为止。常见于：
- **英雄联盟（LoL）**：充能型技能（如烬 Q）
- **守望先锋（Overwatch）**：多段充能技能（如路霸钩子 2 次）
- **Apex Legends**：疾跑充能系统

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 技能决定
- **InteractionMode**: 技能决定

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_Q_charged.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q_charged",
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
// Effect 模板:  mods/<yourMod>/Effects/ability_Q_charged_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// 门控阶段：检查充能数 > 0
Phase OnPropose Gate:
  LoadContextSource         E[0]          // 施法者
  LoadAttribute             E[0], Charges → F[0]
  ConstFloat                1.0           → F[1]
  CompareGtFloat            F[0], F[1]   → B[0]  // Charges > 1?（注：需 > 0，即 > 0.5）
  JumpIfFalse               B[0], FAIL_LABEL

// 执行阶段：消耗充能 + 技能效果 + 启动回复计时器
Phase OnApply Main:
  LoadContextSource         E[0]
  LoadContextTarget         E[1]
  // 消耗 1 次充能
  ConstFloat                -1.0          → F[0]
  ModifyAttributeAdd        E[effect], E[0], Charges, F[0]
  // 技能主效果
  LoadAttribute             E[0], BaseDamage → F[1]
  LoadConfigFloat           "DamageCoeff"   → F[2]
  MulFloat                  F[1], F[2]      → F[3]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[3]
  // 启动充能回复计时（授予计时 Effect）
  ApplyEffectTemplate       E[0], "Effect.Ability.Q.ChargeRefill"
```

充能回复 Effect 模板（每次回复 1 次充能）：
```json5
{
  "id": "Effect.Ability.Q.ChargeRefill",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "durationTicks": 300 },  // 回复 1 次需 5 秒
  "phaseListeners": [
    {
      "phase": "OnExpire",
      "graph": [
        // OnExpire: 回复 1 次充能
        { "op": "LoadContextSource", "E": 0 },
        { "op": "ConstFloat", "value": 1.0, "F": 0 },
        { "op": "ModifyAttributeAdd", "entity": "E[0]", "attr": "Charges", "delta": "F[0]" },
        // 若充能 < 最大值，继续授予下一个回复 Effect
        { "op": "LoadAttribute", "entity": "E[0]", "attr": "Charges", "F": 1 },
        { "op": "LoadAttribute", "entity": "E[0]", "attr": "MaxCharges", "F": 2 },
        { "op": "CompareGtFloat", "a": "F[2]", "b": "F[1]", "B": 0 },
        { "op": "JumpIfFalse", "cond": "B[0]", "label": "DONE" },
        { "op": "ApplyEffectTemplate", "template": "Effect.Ability.Q.ChargeRefill" }
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
| GraphOps.LoadAttribute (10) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.CompareGtFloat (30) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.ModifyAttributeAdd (210) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps.ApplyEffectTemplate (200) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AbilityActivationBlockTags.RequiredAll | `src/Core/Gameplay/GAS/AbilitySpec.cs` | ✅ 已有 — 对应 RequiredAll 字段；Attribute Precondition 仍为 P1 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P1 | 技能激活时需检查 `Charges > 0`，复用 T1/G8 需求。 |

---

## 最佳实践

- **DO**: 用 `Charges` Attribute（Fix64 整数语义）存储充能次数，不要用 Tag 计数。
- **DO**: 充能回复通过 `OnExpire` Phase 的 Effect 链式触发（OnExpire → 回复 1 次 → 若未满则再授予下一个回复 Effect），不要用单独 System。
- **DO**: 在 UI 上显示当前充能数（小圆点/数字）和下次回复倒计时。
- **DON'T**: 不要将充能与冷却混淆：充能有多次，只有全部用完才需等待；冷却是 1 次用完需等待。
- **DON'T**: 不要在 Effect 执行中直接调用 `World.Create` 创建计时器实体，通过 `RuntimeEntitySpawnQueue` 处理。

---

## 验收口径

### 场景 1: 充能消耗与回复

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Charges=2（初始），施放技能两次 |
| **预期输出** | 第一次施放后 Charges=1，第二次施放后 Charges=0；300 ticks 后 Charges=1，600 ticks 后 Charges=2 |
| **Log 关键字** | `[GAS] ModifyAttribute Charges 2→1` <br> `[GAS] ModifyAttribute Charges 1→0` <br> `[GAS] TagExpired Effect.Ability.Q.ChargeRefill -> Charges 0→1` |
| **截图要求** | 技能图标显示充能点数（2→1→0），回复时逐步增加 |
| **多帧录屏** | F0: 施放1(2→1) → F1: 施放2(1→0) → F301: 回复1(0→1) → F601: 回复2(1→2) |

### 场景 2: 充能耗尽无法施放

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Charges=0，尝试施放技能 |
| **预期输出** | 激活失败，UI 提示"充能不足" |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=ability_Q_charged reason=NoCharges Charges=0` |
| **截图要求** | 技能图标显示 0/N，尝试施放时图标轻微抖动 |
| **多帧录屏** | 仅按键帧，后续帧静止 |

### 场景 3: 充能加速

| 项 | 内容 |
|----|------|
| **输入** | 施法者持有"充能回复速度 +100%"效果，回复计时器减半 |
| **预期输出** | 充能在 150 ticks 后回复（而非 300 ticks） |
| **Log 关键字** | `[GAS] GrantedTag Effect.Ability.Q.ChargeRefill duration=150 (ChargeSpeedBonus)` |
| **截图要求** | 充能回复进度条速度加快 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/Resource/ChargesTests.cs
[Test]
public void T3_UseCharge_DecrementsCount()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, charges: 2, maxCharges: 2, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_charged");
    world.Tick(3);

    // Assert
    Assert.AreEqual(1, GetAttribute(world, source, "Charges"));
    Assert.AreEqual(200, GetAttribute(world, target, "Health"));  // FinalDamage=300
}

[Test]
public void T3_ZeroCharges_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, charges: 0, maxCharges: 2);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_charged");
    world.Tick(3);

    // Assert
    Assert.AreEqual(0, GetAttribute(world, source, "Charges"));  // 未消耗
    Assert.AreEqual(500, GetAttribute(world, target, "Health")); // 未扣血
}

[Test]
public void T3_Charge_RefillsAfterTimer()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, charges: 2, maxCharges: 2, baseDamage: 200);
    var target = SpawnUnit(world, hp: 1000);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_charged");
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q_charged");
    world.Tick(3);
    Assert.AreEqual(0, GetAttribute(world, source, "Charges"));

    world.Tick(300);  // 等待回复

    // Assert
    Assert.AreEqual(1, GetAttribute(world, source, "Charges"));  // 回复 1 次
}
```

### 集成验收
1. 运行 `dotnet test --filter "T3"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/resource/t3_charges_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/resource/t3_full.png`（充能满）
   - `artifacts/acceptance/resource/t3_empty.png`（充能耗尽）
4. 多帧录屏：
   - `artifacts/acceptance/resource/t3_60frames.gif`
   - 标注关键帧：充能消耗帧、耗尽帧、回复计时帧、回复完成帧

---

## 参考案例

- **英雄联盟（LoL）烬 Q 充能**: 最多 3 次充能，每次施放消耗 1 次，独立回复计时（8 秒/次），提供灵活使用窗口。
- **守望先锋（Overwatch）路霸双钩**: 2 次充能，允许连续使用，但需等待双倍回复时间才能恢复。
- **Apex Legends 疾跑充能**: 疾跑消耗体力条（可视为充能），停止奔跑后自动回复。
