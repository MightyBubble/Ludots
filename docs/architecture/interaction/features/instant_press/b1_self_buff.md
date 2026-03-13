# B1: 自我增益/即时 Buff

> 按键立即对自身施加属性增益或特殊状态，无需选取目标。典型案例：Dota BKB（魔法免疫）、LoL 奥拉夫 R（无视控制）、《黑暗之魂》系列嗑药。

---

## 机制描述

玩家按下技能键，不需要任何目标选取，立即对施法者自身施加持续时间内的属性 Modifier 或状态标签（GrantedTags）。与被动不同，此机制有冷却和法力消耗；与控制类技能不同，效果完全作用于施法者本人，无需网络目标同步。核心行为：EffectSignal 在 tick 0 触发 → Buff Effect 施加到施法者 → Modifier 生效 → GrantedTags 挂载 → 持续时间到期后 Effect 移除并撤销 Modifier 与标签。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `None`
- **InteractionMode**: `SmartCast`（无目标选取，按键即施放）

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_self_buff.json
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
// Effect 模板:   mods/<yourMod>/Effects/Effect.Ability.B1.Buff.json
// 注册中心:      src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnApply Main:
  LoadContextSource         E[0]               // 施法者（自身）
  ModifyAttributeAdd        E[0], AttackDamage, F[0]   // F[0] 由 Modifier 系统注入，op=Multiply 1.5 走 Modifier 路径
```

> B1 的核心增益不走 Graph 内 ModifyAttributeAdd 直接写值，而是依赖 Effect 的 `modifiers` 声明（Multiply/Add），Graph 只负责可选的条件判断（如检查已有 Buff 层数）。GrantedTags 由 Effect 生命周期自动挂载/移除，无需 Graph 干预。

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.B1.Buff",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 300 },
  "modifiers": [
    { "attribute": "AttackDamage", "op": "Multiply", "value": 1.5 }
  ],
  "grantedTags": [
    { "tag": "Status.Empowered", "formula": "Fixed", "amount": 1 }
  ],
  "configParams": {}
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| AttributeAggregatorSystem | `src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs` | ✅ 已有 |
| GameplayTagContainer | `src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs` | ✅ 已有 |
| EffectTemplateRegistry | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | ✅ 已有 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| InputOrderMapping (None) | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| AbilityExecSpec | `src/Core/Gameplay/GAS/Components/AbilityExecSpec.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 属性修改走 Effect 的 `modifiers` 声明，不在 Graph 内用 `ModifyAttributeAdd` 硬写数值，确保 Modifier 可叠加/可移除。
- **DO**: 状态标签（如 `Status.Empowered`、`Status.Invulnerable`）通过 `grantedTags` 由 Effect 生命周期管理，Effect 移除时标签自动撤销。
- **DO**: 若需"同一 Buff 不可重复叠加"，在 EffectTemplate 设置 `stackPolicy: Replace` 或 `stackPolicy: Refresh`，不在 Graph 内手动检查。
- **DON'T**: 不允许在 Graph 内直接写 Position / Tag 位，位移用 Displacement Preset，标签用 `grantedTags`。
- **DON'T**: 不允许将冷却逻辑嵌入 Graph；冷却由 AbilitySystem 的 `CooldownComponent` 统一管理。
- **DON'T**: Buff 持续时间不用 `ConstFloat` + 自定义计时器，统一走 `lifetime.ticks`，确保时间轴一致性。

---

## 验收口径

### 场景 1: 正常施放路径

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 Q 键（`PressedThisFrame`），自身 AttackDamage=100，当前无同名 Buff |
| **预期输出** | AttackDamage 变为 150（×1.5 Modifier 生效）；`Status.Empowered` 标签挂载；Buff 持续 300 ticks 后自动移除，AttackDamage 恢复 100 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Interaction.B1SelfBuffBuff -> source=<sid> target=<sid>`；`[GAS] TagGranted Status.Empowered entity=<sid>` |
| **截图要求** | 角色头顶出现 Buff 图标，属性面板数值变化与计算结果一致 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: EffectSignal Propose → F3: OnApply → F4: Modifier 聚合 → F5: UI 更新 |

### 场景 2: 沉默/眩晕状态下施放

| 项 | 内容 |
|----|------|
| **输入** | 自身持有 `Status.Silenced` 或 `Status.Stunned` Tag |
| **预期输出** | 技能激活被 BlockTags 拦截；无 Effect Apply；冷却不消耗 |
| **Log 关键字** | `[GAS] AbilityActivationBlocked reason=BlockedByTag tag=Status.Silenced abilityId=Ability.Interaction.B1SelfBuff` |
| **截图要求** | 技能图标显示禁用状态，无 Buff 图标出现 |
| **多帧录屏** | 仅按键帧，后续帧角色无变化 |

### 场景 3: 法力不足

| 项 | 内容 |
|----|------|
| **输入** | 当前 Mana = 0，技能消耗 Cost = 50 |
| **预期输出** | 激活失败；UI 显示法力不足提示；冷却不消耗 |
| **Log 关键字** | `[GAS] AbilityActivationFailed reason=InsufficientResource abilityId=Ability.Interaction.B1SelfBuff attribute=Mana delta=50` |

> 当前 `InteractionShowcaseMod` 的 B1 交付以世界空间 aura + 调试 overlay 作为可视验收面，不接入正式技能栏禁用图标或 Buff HUD 图标；同时该 showcase 验证的是 Mana 前置门槛，不包含成功施放后的 Mana 扣减。
>
> 当前可视证据覆盖 `Status.Silenced` 分支；`Status.Stunned` 作为同一 BlockTags 路径的第二个标签，由 `src/Tests/GasTests/SelfBuffTests.cs` 中的 `B1_Stunned_ActivationBlocked` 单独覆盖，避免后续模板只验证一个阻断标签。
>
> 采样口径说明：headless acceptance 记录的是“固定步运行时里首个同时满足 `stage=buff_active` 且 `EffectiveTag=true` 的样本”；launcher visual 记录的是“首个 presentation checkpoint”。两条证据链的语义仍然不同，但当前提交产物里二者都落在 tick `7`：`visual/summary.json` 的 `active_tick=7`，`002_buff_active.png` 也显示 `ScriptTick=7 / Tick=7`。如果后续 recorder 或采样边界调整导致 visual checkpoint 与 headless strict sample 再次错开，应继续按观测语义分别命名，而不要把不同口径压成同一个“active tick”。

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SelfBuffTests.cs
[Test]
public void B1_BasicBuff_AttributeModifierApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, attackDamage: 100, mana: 100);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, abilityId: "Ability.Interaction.B1SelfBuff");
    world.Tick(3);  // Order → EffectRequest → Apply

    // Assert
    // AttackDamage 应 = 100 * 1.5 = 150（Multiply Modifier）
    Assert.AreEqual(Fix64.FromInt(150), GetAttribute(world, source, "AttackDamage"));
}

[Test]
public void B1_BuffExpiry_ModifierRemoved()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, attackDamage: 100, mana: 100);
    world.SubmitOrder(source, OrderType.CastAbility, abilityId: "Ability.Interaction.B1SelfBuff");
    world.Tick(3);

    // Buff 持续 300 ticks，到期后 Modifier 移除
    world.Tick(301);
    Assert.AreEqual(Fix64.FromInt(100), GetAttribute(world, source, "AttackDamage"));
}

[Test]
public void B1_Silenced_ActivationBlocked()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, attackDamage: 100, mana: 100);
    GrantTag(world, source, "Status.Silenced");

    world.SubmitOrder(source, OrderType.CastAbility, abilityId: "Ability.Interaction.B1SelfBuff");
    world.Tick(3);

    // 沉默状态下 Buff 不应生效
    Assert.AreEqual(Fix64.FromInt(100), GetAttribute(world, source, "AttackDamage"));
}

[Test]
public void B1_Stunned_ActivationBlocked()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, attackDamage: 100, mana: 100);
    GrantTag(world, source, "Status.Stunned");

    world.SubmitOrder(source, OrderType.CastAbility, abilityId: "Ability.Interaction.B1SelfBuff");
    world.Tick(3);

    Assert.AreEqual(Fix64.FromInt(100), GetAttribute(world, source, "AttackDamage"));
}

[Test]
public void B1_InsufficientMana_ActivationFailed()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, attackDamage: 100, mana: 0);

    world.SubmitOrder(source, OrderType.CastAbility, abilityId: "Ability.Interaction.B1SelfBuff");
    world.Tick(3);

    Assert.AreEqual(Fix64.FromInt(100), GetAttribute(world, source, "AttackDamage"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "B1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
  - `artifacts/acceptance/interaction-b1-self-buff/trace.jsonl`
3. 截图存档：
  - `artifacts/acceptance/interaction-b1-self-buff/visual/screens/002_buff_active.png`
  - `artifacts/acceptance/interaction-b1-self-buff/visual/screens/004_silenced_blocked.png`
  - `artifacts/acceptance/interaction-b1-self-buff/visual/screens/005_insufficient_mana.png`
4. 多帧录屏：
  - `artifacts/acceptance/interaction-b1-self-buff/visual/interaction-b1-self-buff.gif`
  - `artifacts/acceptance/interaction-b1-self-buff/visual/interaction-b1-self-buff.mp4`
  - 标注关键帧：`000_start`、`001_order_submitted`、`002_buff_active`、`003_buff_expired`、`004_silenced_blocked`、`005_insufficient_mana`

---

## 参考案例

- **Dota 2 BKB（黑皇杖）**: 按键立即获得魔法免疫，持续时间内附带 `Status.MagicImmune` Tag，绝大多数控制技能的 RequiredNotTag 检测失败从而不能作用于持有该 Tag 的单位；Modifier 叠加层数与冷却管理是 Buff 机制最典型范例。
- **LoL 奥拉夫 R（无畏战吼）**: 激活后清除并免疫控制（RemoveTags + BlockTags），同时提升攻速（Multiply Modifier）；展示了"激活时主动净化 + 期间免疫"的双重 Buff 语义，可用 `grantedTags` + `removeTags` 组合实现。
- **《黑暗之魂》系列——伊斯特尔荆棘药**: 按键嗑药 → 即时 HP 回复（ModifyAttributeAdd Health +X）；与持续 Buff 的区别在于 `lifetime: Instant`，无 Modifier 残留。
