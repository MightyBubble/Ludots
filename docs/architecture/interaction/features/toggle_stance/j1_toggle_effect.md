# J1: Toggle 开/关（持续消耗效果）

> 按键切换一个持续性效果的开启与关闭。典型案例：LoL Singed Q（毒液拖尾）、Dota Viper Poison Attack、Dota Rot。

---

## 机制描述

玩家每次按下技能键，若当前效果处于"关"状态则开启（施加 Buff、启动周期 DoT/费用消耗），若处于"开"状态则关闭（移除 Buff、停止消耗）。状态由一个 Toggle Tag 表示：Tag 存在 = 开，Tag 不存在 = 关。交互层本身不感知 toggle 状态，判断逻辑全部在 Graph/AbilityExecSystem 层。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `None`
- **InteractionMode**: `SmartCast`（无需目标，按即生效）

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_toggle.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "None",
  "isSkillMapping": true,
  "castModeOverride": null
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/Effect.Ability.J1.Toggle.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnApply Main:
  LoadContextSource           E[0]                   // 施法者（Toggle 持有者）
  HasTag                      E[0], "toggle_active"  → B[0]
  JumpIfFalse                 B[0], @label_turnOn

  // 已开启 → 关闭
  // 通过移除 Buff Effect 实现：AbilityExecSystem 检测到 tag 丢失，停止周期 effect
  ApplyEffectTemplate         E[0], E[0], "Effect.Ability.J1.RemoveBuff"
  Jump                        @label_end

@label_turnOn:
  // 未开启 → 施加持续 Buff
  ApplyEffectTemplate         E[0], E[0], "Effect.Ability.J1.Buff"

@label_end:
  // 无操作，Graph 结束
```

Effect 模板（Buff 开启）：
```json5
{
  "id": "Effect.Ability.J1.Buff",
  "presetType": "Buff",
  "lifetime": { "kind": "Infinite" },
  "grantedTags": ["toggle_active"],
  "configParams": {
    "ManaPerTick": 5,
    "TickIntervalTicks": 30
  },
  "phaseListeners": []
}
```

Effect 模板（关闭 — 移除 Buff）：
```json5
{
  "id": "Effect.Ability.J1.RemoveBuff",
  "presetType": "RemoveEffect",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "TargetEffectId": "Effect.Ability.J1.Buff"
  }
}
```

周期消耗（绑定到 Buff 生命周期）：
```json5
{
  "id": "Effect.Ability.J1.PeriodicCost",
  "presetType": "PeriodicSearch",
  "lifetime": { "kind": "WhileTagPresent", "tagId": "toggle_active" },
  "configParams": {
    "Period": 30,
    "ManaCost": 5
  }
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| AbilityToggleSpec | `src/Core/Gameplay/GAS/AbilityDefinition.cs` | ✅ 已有 |
| GameplayTagContainer | `src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs` | ✅ 已有 |
| HasTag (op 33) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| ApplyEffectTemplate (op 200) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| RemoveEffect preset | `src/Core/Gameplay/GAS/EffectPresetType.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。`AbilityToggleSpec` 已有，建议 P2 补充完备性验证测试。

---

## 最佳实践

- **DO**: Toggle 状态只用 Tag 表示，不引入额外布尔属性；Tag 存在 = 开，不存在 = 关，逻辑简单且与 GAS 标准一致。
- **DO**: 周期消耗/DoT 效果生命周期绑定到 `toggle_active` Tag（`WhileTagPresent`），Tag 移除时效果自动停止，无需手动清理。
- **DO**: 关闭时走 `RemoveEffect` preset，不在 Graph 内直接操作 Tag 位；Tag 由 Effect 生命周期统一管理。
- **DON'T**: 不允许在 Graph 内直接写 Tag 位（CC Tag、stance Tag 一律通过 `GrantedTags` + Effect 生命周期管理）。
- **DON'T**: 不要用 `Held` trigger 做 toggle；`Held` 会每帧触发，需用 `PressedThisFrame`。
- **DON'T**: 不要在两个独立 Effect 里各自检查 tag；将 HasTag 判断集中在单个 Graph Phase 的 JumpIfFalse 分支内。

---

## 验收口径

### 场景 1: 首次按键 — 开启效果

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Mana=200，无 `toggle_active` Tag，按下 Q 键（`PressedThisFrame`） |
| **预期输出** | `toggle_active` Tag 被添加；Buff Effect 持续存在；每 30 tick 消耗 5 Mana |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.J1.Buff -> source=<sid> target=<sid>` |
| **截图要求** | 技能图标显示激活状态（高亮/发光），Mana 条持续减少 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: HasTag=false 分支 → F3: Buff Apply → F30: 首次扣 Mana |

### 场景 2: 再次按键 — 关闭效果

| 项 | 内容 |
|----|------|
| **输入** | 施法者持有 `toggle_active` Tag，再次按下 Q 键 |
| **预期输出** | `toggle_active` Tag 被移除；Buff Effect 停止；Mana 不再消耗 |
| **Log 关键字** | `[GAS] RemoveEffect Effect.Ability.J1.Buff -> target=<sid>` |
| **截图要求** | 技能图标恢复未激活状态，Mana 条停止变化 |
| **多帧录屏** | F0: 按键 → F1: HasTag=true 分支 → F2: RemoveBuff Apply → F3: Tag 消失 → F33: Mana 不变 |

### 场景 3: Mana 耗尽后自动关闭

| 项 | 内容 |
|----|------|
| **输入** | 施法者 Mana=3（不足一次扣除），Toggle 已开启 |
| **预期输出** | 周期消耗检查 Mana 不足 → 触发 `InsufficientResource` 事件 → Buff 被移除 |
| **Log 关键字** | `[GAS] AbilityActivationFailed reason=InsufficientMana` 或 `[GAS] RemoveEffect Effect.Ability.J1.Buff reason=ResourceDepleted` |
| **截图要求** | 技能图标自动变为未激活，UI 提示"法力不足" |
| **多帧录屏** | 效果运行帧 → Mana 降至 0 → 自动移除帧 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ToggleEffectTests.cs
[Test]
public void J1_FirstPress_ActivatesBuffAndGrantsTag()
{
    // Arrange
    var world = CreateTestWorld();
    var caster = SpawnUnit(world, mana: 200);
    Assert.IsFalse(HasTag(world, caster, "toggle_active"));

    // Act
    world.SubmitOrder(caster, OrderType.CastAbility, Entity.Null, abilityId: "ability_toggle_Q");
    world.Tick(3);

    // Assert
    Assert.IsTrue(HasTag(world, caster, "toggle_active"));
    Assert.IsTrue(HasActiveEffect(world, caster, "Effect.Ability.J1.Buff"));
}

[Test]
public void J1_SecondPress_DeactivatesBuffAndRemovesTag()
{
    // Arrange
    var world = CreateTestWorld();
    var caster = SpawnUnit(world, mana: 200);
    world.SubmitOrder(caster, OrderType.CastAbility, Entity.Null, abilityId: "ability_toggle_Q");
    world.Tick(3); // 开启

    // Act
    world.SubmitOrder(caster, OrderType.CastAbility, Entity.Null, abilityId: "ability_toggle_Q");
    world.Tick(3); // 关闭

    // Assert
    Assert.IsFalse(HasTag(world, caster, "toggle_active"));
    Assert.IsFalse(HasActiveEffect(world, caster, "Effect.Ability.J1.Buff"));
}

[Test]
public void J1_PeriodicCost_DrainsManaEvery30Ticks()
{
    // Arrange
    var world = CreateTestWorld();
    var caster = SpawnUnit(world, mana: 100);
    world.SubmitOrder(caster, OrderType.CastAbility, Entity.Null, abilityId: "ability_toggle_Q");
    world.Tick(3);

    // Act
    world.Tick(30); // 1 个周期

    // Assert: 100 - 5 = 95
    Assert.AreEqual(95, GetAttribute(world, caster, "Mana"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "J1"` — 全绿。
2. 在 Playground 中录制 120 帧日志，存档：
   - `artifacts/acceptance/toggle_stance/j1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/toggle_stance/j1_normal.png`（开启状态）
   - `artifacts/acceptance/toggle_stance/j1_edge.png`（Mana 耗尽自动关闭）
4. 多帧录屏：
   - `artifacts/acceptance/toggle_stance/j1_60frames.gif`
   - 标注关键帧：按键帧、Tag 添加帧、首次扣 Mana 帧、关闭帧

---

## 参考案例

- **LoL Singed Q（毒液拖尾）**: 按 Q 开启/关闭拖尾 DoT，持续消耗 Mana，状态由图标高亮反馈。
- **Dota Viper Poison Attack**: 主动开关叠加减速毒效果，切换时无冷却，资源消耗绑定效果生命周期。
- **Dota Rot（Pudge W）**: 开关式 AoE 持续伤害，对自身和周围敌人同时造成伤害，关闭即停止。
