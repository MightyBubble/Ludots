# T2: 冷却（Cooldown）

> 技能释放后进入冷却期，冷却期间无法再次释放。如：LoL/Dota 2 几乎所有技能型游戏的基础冷却机制。

---

## 机制描述

技能释放后，施法者获得一个冷却 Tag，持有该 Tag 时技能被屏蔽（BlockedAny），冷却 Tag 到期后技能恢复可用。常见于：
- **英雄联盟（LoL）**：所有技能的冷却倒计时
- **Dota 2**：技能冷却时间
- **魔兽世界（WoW）**：技能冷却（全局冷却/技能冷却）

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
  "isSkillMapping": true
}
```

---

## Graph 实现

冷却通过 `GrantedTags` + `BlockedAny` 机制实现：技能释放时授予冷却 Tag（持续 N ticks），AbilitySpec 检查到持有冷却 Tag 时屏蔽技能。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/ability_Q_cooldown.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// AbilitySpec 配置：
// BlockedAny: ["ability_Q.cd"]   ← 持有此 Tag 时技能不可用

// 技能释放后，授予冷却 Tag：
Phase OnApply Main:
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标
  // 技能主效果
  LoadAttribute             E[0], BaseDamage → F[0]
  LoadConfigFloat           "DamageCoeff"   → F[1]
  MulFloat                  F[0], F[1]      → F[2]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[2]
  // 授予冷却 Tag（通过独立 Effect）
  ApplyEffectTemplate       E[0], "Effect.Ability.Q.Cooldown"
```

冷却 Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Q.Cooldown",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "durationTicks": 180 },  // 冷却 3 秒 = 180 ticks
  "grantedTags": ["ability_Q.cd"],                           // 持有此 Tag 时屏蔽技能
  "phaseListeners": []
}
```

AbilitySpec 配置示例：
```json5
{
  "id": "ability_Q",
  "blockedByAnyTags": ["ability_Q.cd"],  // 持有冷却 Tag 时不可激活
  "effectTemplates": ["Effect.Ability.Q.Main"]
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GrantedTags（Duration 生命周期） | `src/Core/Gameplay/GAS/Components/GrantedTagsBuffer.cs` | ✅ 已有 |
| AbilitySpec.BlockedByAnyTags | `src/Core/Gameplay/GAS/AbilitySpec.cs` | ✅ 已有 |
| GraphOps.ApplyEffectTemplate (200) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 无 | - | 现有基建可完整表达。`GrantedTags` + `Duration` Effect + `BlockedByAnyTags` 已覆盖冷却机制全部需求。 |

---

## 最佳实践

- **DO**: 冷却通过 `GrantedTags` + Duration Effect 实现，Tag 到期即冷却结束，无需单独的 CooldownSystem。
- **DO**: 冷却 Effect 与主效果分离，方便动态修改冷却时间（修改 Effect lifetime 即可）。
- **DO**: 冷却缩减通过 `ModifyAttributeAdd` 修改剩余 ticks 实现（或用 Modifier 改变 Duration 速率）。
- **DON'T**: 不要用 Attribute（cooldown_remaining）实现冷却——Tag 生命周期更简洁，自动清理无需手动递减。
- **DON'T**: 不要在 Graph 中手动维护冷却状态，`GrantedTags` 系统自动管理。

---

## 验收口径

### 场景 1: 冷却期间尝试重复施放

| 项 | 内容 |
|----|------|
| **输入** | 施法者施放 Q 技能后，立即再次施放 Q |
| **预期输出** | 第二次施放失败，提示"技能冷却中"；冷却 Tag 持有期间技能不可用 |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=ability_Q reason=BlockedByTag tag=ability_Q.cd` |
| **截图要求** | 技能图标显示冷却读条，尝试施放时图标轻微抖动 |
| **多帧录屏** | F0: 施放 Q → F1: 冷却 Tag 授予 → F2-F181: 冷却中 → F181: 冷却结束 → F182: 可再次施放 |

### 场景 2: 冷却结束后正常施放

| 项 | 内容 |
|----|------|
| **输入** | 等待冷却结束（180 ticks），再次按 Q |
| **预期输出** | 技能正常施放，新的冷却 Tag 授予 |
| **Log 关键字** | `[GAS] TagExpired ability_Q.cd at tick=181` <br> `[Input] OrderSubmitted actionId=ability_Q` <br> `[GAS] GrantedTag ability_Q.cd duration=180` |
| **截图要求** | 技能图标恢复完整，施放后再次显示冷却读条 |
| **多帧录屏** | F181: 冷却结束 → F182: 按键 → F183: 施放 → F184: 新冷却开始 |

### 场景 3: 冷却缩减

| 项 | 内容 |
|----|------|
| **输入** | 施法者持有"冷却缩减 50%"效果，冷却 Tag 持续时间应为 90 ticks |
| **预期输出** | 冷却 Tag 在 90 ticks 后到期，技能可用 |
| **Log 关键字** | `[GAS] GrantedTag ability_Q.cd duration=90 (CDR_50%)` <br> `[GAS] TagExpired ability_Q.cd at tick=90` |
| **截图要求** | 冷却读条速度为正常 2 倍 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/Resource/CooldownTests.cs
[Test]
public void T2_Cooldown_BlocksRecast()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, mana: 200);
    var target = SpawnUnit(world, hp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(1);
    // 立即再施放
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(2);

    // Assert
    // 只有第一次有效，第二次被冷却 Tag 屏蔽
    Assert.IsTrue(HasTag(world, source, "ability_Q.cd"));
    Assert.AreEqual(200, GetAttribute(world, target, "Health")); // 只被攻击一次
}

[Test]
public void T2_Cooldown_Expires_EnablesRecast()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, mana: 200);
    var target = SpawnUnit(world, hp: 1000);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(181);  // 等过冷却期（180 ticks）
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    Assert.IsFalse(HasTag(world, source, "ability_Q.cd"));  // 冷却已结束
    Assert.AreEqual(400, GetAttribute(world, target, "Health"));  // 被攻击两次 500-2*300
}

[Test]
public void T2_CDR_ShortensCooldown()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, mana: 200);
    ApplyCooldownReductionEffect(world, source, cdrPercent: 50);  // CDR 50%
    var target = SpawnUnit(world, hp: 1000);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(91);  // 90 ticks 冷却（50% CDR）
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "ability_Q");
    world.Tick(3);

    // Assert
    Assert.AreEqual(400, GetAttribute(world, target, "Health"));  // 两次成功施放
}
```

### 集成验收
1. 运行 `dotnet test --filter "T2"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/resource/t2_cooldown_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/resource/t2_active.png`（技能可用状态）
   - `artifacts/acceptance/resource/t2_cooldown.png`（冷却中状态）
4. 多帧录屏：
   - `artifacts/acceptance/resource/t2_60frames.gif`
   - 标注关键帧：施放帧、冷却开始帧、再次尝试（失败）帧、冷却结束帧

---

## 参考案例

- **英雄联盟（LoL）冷却系统**: 技能有独立冷却，出装可提升冷却缩减（CDR）最多 45%。
- **Dota 2 冷却时间**: 类似系统，但冷却时间通常更长（数十秒），可通过技能自身升级缩减。
- **魔兽世界（WoW）全局冷却（GCD）**: 每次施法触发 1.5 秒全局冷却，同时技能有独立冷却。
