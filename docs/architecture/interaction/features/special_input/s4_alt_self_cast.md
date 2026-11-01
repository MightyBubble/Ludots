# S4: Alt 自我施放（Alt Self Cast）

> 友方或自身目标来自技能引用的 `graph.aim.pick.friendly`。Dota 2 里 Alt+治疗打自己，是这页要对齐的玩家手感；修饰键本身不改目标。

---

## 机制描述

指针落在友方或自己身上时，`graph.aim.pick.friendly` 选出那个单位。常见参照：
- **Dota 2**：Alt+治疗 = 自我治疗
- **英雄联盟（LoL）**：Alt+护盾 = 自我护盾
- **风暴英雄（HotS）**：Alt+增益技能 = 自我增益

与 S1（组合键）的区别：S4 是同一技能的目标覆盖，S1 是完全不同的技能 ID。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **TargetType**: `Entity`（`graph.aim.pick.friendly`）
- **手势**: 偏好 `pref.aim.cast_mode`

Alt 绑在 `Camera.Rig` 的 `AlternateModifier` 上。技能数据里没有强制自身的字段。打到自己或友方时，技能在 `triggerGraphs` 里引用 `graph.aim.rule.unit_only` 和 `graph.aim.pick.friendly`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


---

## Graph 实现

Alt 自我施放的 Graph 实现与普通目标施放完全相同，差异仅在目标选择。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/heal_spell_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标（拾取图选出的单位）
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
  "lifetime": "Instant",
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
| 瞄准规则图 | `mods/capabilities/input/AimInteractionMod` | 技能在 `triggerGraphs` 引用 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

无。友方或自身目标写在技能引用的 `graph.aim.pick.friendly`。


---

## 最佳实践

- **DO**: 为自我施放提供视觉反馈（如技能图标边框变色），提示玩家 Alt 键已生效。
- **DO**: 自我施放与普通施放共享相同的冷却和资源消耗。
- **DO**: 在 UI 中显示 Alt 键提示（如技能图标下方显示"Alt: 自我施放"）。
- **DON'T**: 不要在技能数据里加强制自身字段。目标规则和拾取写在技能引用的图上。
- **DON'T**: 不要把 Alt 写成另一套手势。手势仍是 `pref.aim.cast_mode`。友方目标写在 `graph.aim.pick.friendly`。

---

## 验收口径

### 场景 1: Alt + 技能（自我施放）

| 项 | 内容 |
|----|------|
| **输入** | 技能引用 `graph.aim.pick.friendly`，指针在施法者身上，按下治疗技能键，当前 HP=300/500 |
| **预期输出** | 治疗施法者自身，HP 恢复到 450/500（假设 HealAmount=150） |
| **Log 关键字** | 目标来自 `graph.aim.pick.friendly` <br> `[GAS] ApplyEffect Effect.Ability.HealSpell.Main -> source=<self_id> target=<self_id> HealAmount=150` |
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
public void S4_FriendlyPick_HealsSource()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 300, maxHp: 500, healPower: 100, mana: 100);
    var ally = SpawnUnit(world, hp: 200, maxHp: 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, source, abilityId: "heal_spell");
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
public void S4_FriendlyPick_InsufficientMana_ActivationFailed()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, hp: 300, maxHp: 500, mana: 10);  // Cost = 50

    // Act
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
