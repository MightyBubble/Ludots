# C1: 敌方单位伤害

> 对选中的敌方单位造成即时伤害，通过完整伤害管线（OnCalculate 写 Blackboard → OnApply 读取扣血）执行。典型案例：LoL 暗影刺客 Q（单体物理伤害）、Dota 巫妖 Frostbite（冰冻+伤害）。

---

## 机制描述

玩家按下技能键（PressedThisFrame），系统取光标下或预选的敌方单位作为目标，立即发出 CastAbility 订单。订单验证通过后，GAS 在当前 Tick 生成 Effect，OnCalculate 阶段将施法者的 BaseDamage 属性乘以配置系数写入 Blackboard（DamageAmount），OnApply Listener 读取 Armor 做减伤计算并写入 FinalDamage，OnApply Main 将 FinalDamage 以负数 delta 应用到目标的 Health 属性。

与 AoE 伤害的区别：目标是单一 Entity（由 InputOrderMapping.selectionType=Entity 保证），不涉及范围查询。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Entity`
- **InteractionMode**: `SmartCast`（推荐）/ `TargetFirst`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_q.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "castModeOverride": null,              // null = 跟随全局 InteractionMode
  "autoTargetPolicy": "NearestEnemyInRange",
  "autoTargetRangeCm": 600
}
```

SmartCast 模式下：按键瞬间取光标下最近敌方单位；若光标下无有效目标则按 `autoTargetPolicy` 回退到射程内最近敌方单位。TargetFirst 模式下：先点选目标，再按键确认。

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:   mods/<yourMod>/Effects/Effect.Ability.C1.Main.json
// 注册中心:      src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]                          // 施法者
  LoadContextTarget         E[1]                          // 目标（敌方单位）
  LoadAttribute             E[0], BaseDamage    → F[0]   // 施法者基础攻击力
  LoadConfigFloat           "DamageCoeff"       → F[1]   // 配置系数，如 1.5
  MulFloat                  F[0], F[1]          → F[2]   // 理论伤害
  WriteBlackboardFloat      E[effect], DamageAmount, F[2]

Phase OnApply Listener (priority=200, scope=Target):      // 护甲减伤
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  LoadAttribute             E[1], Armor               → F[1]
  ConstFloat                100.0                     → F[2]
  AddFloat                  F[2], F[1]                → F[3]   // 100 + Armor
  DivFloat                  F[2], F[3]                → F[4]   // 100 / (100+Armor)
  MulFloat                  F[0], F[4]                → F[5]   // FinalDamage
  WriteBlackboardFloat      E[effect], FinalDamage, F[5]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], FinalDamage → F[0]
  NegFloat                  F[0]                   → F[1]
  ModifyAttributeAdd        E[effect], E[1], Health, F[1]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.C1.Main",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 1.5
  },
  "grantedTags": [],
  "phaseListeners": [
    {
      "phase": "OnApply",
      "priority": 200,
      "scope": "Target",
      "graphProgramId": "Graph.Ability.C1.ArmorMitigation"
    }
  ]
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs` | ✅ 已有 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| SelectionRuleRegistry | `src/Core/Input/Selection/SelectionRuleRegistry.cs` | ✅ 已有 |
| TargetFilter.Hostile | `src/Core/Gameplay/GAS/TargetFilter.cs` | ✅ 已有 |
| ModifyAttributeAdd (op 210) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AutoTargetPolicy.NearestEnemyInRange | `src/Core/Input/Orders/AutoTargetPolicy.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 伤害公式走 `OnCalculate` Graph + Blackboard，不在 `OnApply` 里硬编码数值。
- **DO**: 护甲减伤实现为 `OnApply Listener`（priority=200），使护盾等其他 Listener 可在更低优先级介入。
- **DO**: SmartCast 配合 `autoTargetPolicy` 保证光标离目标稍远时仍可命中，提升操作容错。
- **DON'T**: 不允许在 Graph 内直接修改 HP 字段（绕过 Modifier 聚合），必须走 `ModifyAttributeAdd`。
- **DON'T**: 不允许在 Graph 阶段做结构变更（如创建伤害数字实体），伤害浮字由表现层 Observer 响应 HP 变化事件驱动。
- **DON'T**: CC Tag（Status.Stunned 等）不能由 Graph 直接写位，必须通过 `GrantedTags` + Effect 生命周期管理。

---

## 验收口径

### 场景 1: 正常施放路径

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 Q 键（`PressedThisFrame`），SmartCast，光标悬停敌方单位，目标 HP=500, Armor=50, 施法者 BaseDamage=200 |
| **预期输出** | `DamageAmount = 200 × 1.5 = 300`；`FinalDamage = 300 × 100/(100+50) = 200`；目标 HP 降至 300 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.C1.Main -> source=<sid> target=<tid> FinalDamage=200` |
| **截图要求** | 伤害浮字"200"出现在目标头顶，目标血条对应减少 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: Effect Propose → F4: OnApply → F5: HP 变化(-200) → F6: 浮字出现 |

### 场景 2: 目标无效（死亡/不可选中）

| 项 | 内容 |
|----|------|
| **输入** | 目标 HP ≤ 0 或持有 `Status.Untargetable` Tag；玩家按 Q 键 |
| **预期输出** | 订单被丢弃；无 Effect 生成；无 HP 变化 |
| **Log 关键字** | `[Input] OrderDiscarded reason=InvalidTarget` |
| **截图要求** | 无浮字，无命中视觉效果 |
| **多帧录屏** | 仅 F0 按键帧，后续帧画面静止，无血条变化 |

### 场景 3: 超出射程

| 项 | 内容 |
|----|------|
| **输入** | 目标距施法者 > `autoTargetRangeCm`（600cm），光标未悬停任何单位 |
| **预期输出** | SmartCast 回退无有效目标，订单不生成；TargetFirst 模式下显示"目标超出射程"提示 |
| **Log 关键字** | `[Input] OrderDiscarded reason=OutOfRange` |
| **截图要求** | UI 提示文字出现，角色无动作 |
| **多帧录屏** | 按键帧 + 静止帧 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/UnitTargetTests/C1HostileUnitDamageTests.cs
[Test]
public void C1_BasicCase_CorrectDamageApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500, armor: 50, team: Team.Enemy);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "Ability.C1");
    world.Tick(3);  // Order → EffectRequest → Apply

    // Assert
    // FinalDamage = 200 * 1.5 * 100/(100+50) = 200
    Assert.AreEqual(300, GetAttribute(world, target, "Health"));
}

[Test]
public void C1_TargetDead_EffectNotApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 0, team: Team.Enemy);  // 已死亡

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "Ability.C1");
    world.Tick(3);

    // Assert
    Assert.AreEqual(0, GetAttribute(world, target, "Health"));  // HP 不变
    Assert.AreEqual(0, GetEffectApplyCount(world, "Effect.Ability.C1.Main"));
}

[Test]
public void C1_FriendlyTarget_OrderDiscarded()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, team: Team.Player);
    var ally = SpawnUnit(world, hp: 300, team: Team.Player);  // 友方

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, ally, abilityId: "Ability.C1");
    world.Tick(3);

    // Assert
    // TargetFilter.Hostile 应拦截
    Assert.AreEqual(300, GetAttribute(world, ally, "Health"));
}

[Test]
public void C1_ArmorMitigation_CorrectFormula()
{
    // Arrange: BaseDamage=100, Coeff=1.5, Armor=100 → FinalDamage = 150 * 100/200 = 75
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 100);
    var target = SpawnUnit(world, hp: 500, armor: 100, team: Team.Enemy);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "Ability.C1");
    world.Tick(3);

    // Assert
    Assert.AreEqual(425, GetAttribute(world, target, "Health"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "C1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/unit_target/c1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/unit_target/c1_normal.png`
   - `artifacts/acceptance/unit_target/c1_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/unit_target/c1_60frames.gif`
   - 标注关键帧：按键帧、Effect Apply 帧、HP 变化帧

---

## 参考案例

- **LoL 暗影刺客（Zed） Q — 死亡标记**: 发射技能飞镖对敌方单位造成物理伤害，伤害值基于 AD 系数计算，命中敌方英雄触发额外效果；与 C1 的区别在于增加了飞行物阶段，但伤害管线设计一致。
- **Dota 巫妖（Lich） Frost Blast**: 对单一敌方单位造成魔法伤害并减速，configParams 中存储 DamageCoeff 和 SlowDuration；C1 可通过增加 GrantedTags 模拟此类附加 CC 效果。
- **LoL 赏金猎人（Miss Fortune） Q — 双重射击**: 第一发对目标造成物理伤害，第二发弹射到另一敌方单位；C1 的伤害管线是其单目标阶段的直接原型。
