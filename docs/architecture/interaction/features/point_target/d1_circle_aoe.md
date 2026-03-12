# D1: 圆形 AoE（落点指示器）

> 玩家点击地面选择落点，以该点为圆心对范围内敌方单位造成即时伤害。典型案例：LoL 薇古丝 E（黑暗物质）、Dota 虚空假面大招（超时空领域）、SC2 灵能风暴。

---

## 机制描述

玩家按下技能键后进入 AimCast 瞄准阶段，地面出现圆形指示器随光标移动；松开或二次点击确认落点，立即对圆形范围内所有敌方单位执行一次伤害结算。与单体指向技能的区别在于：目标是"位置"而非"单位"，且命中数量不固定。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Position`
- **InteractionMode**: `AimCast`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_D1.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_D1",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Position",
  "isSkillMapping": true,
  "castModeOverride": null,           // 跟随全局 InteractionMode（默认 AimCast）
  "autoTargetPolicy": null
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/d1_aoe_damage.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]                    // 施法者
  LoadAttribute             E[0], BaseDamage → F[0]
  LoadConfigFloat           "DamageCoeff"   → F[1]
  MulFloat                  F[0], F[1]      → F[2]
  WriteBlackboardFloat      E[effect], DamageAmount, F[2]

Phase OnApply Listener (priority=200, scope=Target):  // 护甲减伤
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  LoadContextTarget         E[1]
  LoadAttribute             E[1], Armor             → F[1]
  // FinalDamage = DamageAmount * 100 / (100 + Armor)
  ConstFloat                100.0  → F[3]
  AddFloat                  F[3], F[1]   → F[4]   // 100 + Armor
  DivFloat                  F[3], F[4]   → F[5]   // 100 / (100 + Armor)
  MulFloat                  F[0], F[5]   → F[6]   // FinalDamage
  WriteBlackboardFloat      E[effect], FinalDamage, F[6]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], FinalDamage → F[0]
  LoadContextTarget         E[1]
  NegFloat                  F[0] → F[1]
  ModifyAttributeAdd        E[effect], E[1], Health, F[1]

// ---- AoE 扩散层（AbilityExecSpec 中的 Search 预设 Effect） ----
// 该 Graph 绑定在 d1_aoe_search.json，执行 AoE 空间查询后对结果列表分发伤害

Phase OnApply Main (Search Effect):
  LoadContextTargetContext  E[2]                  // AoE 中心位置实体（OrderPosition）
  QueryRadius               E[2], 250             // TargetList = 半径250cm内所有实体
  QueryFilterRelationship   Hostile               // 仅敌方
  FanOutApplyEffect         "d1_aoe_damage"       // 对列表每个成员施加伤害 Effect
```

Effect 模板示例：
```json5
// mods/<yourMod>/Effects/d1_aoe_search.json
{
  "id": "Effect.Ability.D1.AoeSearch",
  "presetType": "Search",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "QueryRadiusCm": 250
  },
  "grantedTags": [],
  "phaseListeners": []
}

// mods/<yourMod>/Effects/d1_aoe_damage.json
{
  "id": "Effect.Ability.D1.Damage",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 1.5,
    "DamageType": 1
  },
  "grantedTags": [],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs` | ✅ 已有 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| QueryRadius | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` (op 100) | ✅ 已有 |
| FanOutApplyEffect | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` (op 201) | ✅ 已有 |
| QueryFilterRelationship | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` (op 113) | ✅ 已有 |
| SpatialBlackboardKey | `src/Core/Input/Orders/OrderArgs.cs` | ✅ 已有 |
| EffectTemplateRegistry | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | ✅ 已有 |
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| GroundOverlay Performer | `src/Core/Presentation/Performers/GroundOverlayPerformer.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 伤害公式在 `OnCalculate` Graph 中算出 `DamageAmount` 写入 Blackboard，护甲减伤在 `OnApply Listener` 中读取并覆写 `FinalDamage`，`OnApply Main` 只做最终写 HP，三层分离。
- **DO**: AoE 范围（QueryRadiusCm）放进 `configParams`，避免硬编码在 Graph 里，方便策划热改数值。
- **DO**: `QueryFilterRelationship(Hostile)` 必须在 `QueryRadius` 之后紧跟执行，减少后续 FanOut 的对象数。
- **DO**: GroundOverlay Performer 的 `radius` 与 Effect 模板的 `QueryRadiusCm` 保持同源（从同一配置读取），避免视觉与实际范围不一致。
- **DON'T**: 不在 Graph 内直接操作实体结构（创建/销毁），AoE 只负责查询和分发 Effect。
- **DON'T**: 不在 `OnApply Main` 中重复计算护甲减伤，减伤逻辑必须在 Listener 中处理，便于全局护甲 Mod 拦截。
- **DON'T**: 不允许 `selectionType: None` 用于圆形 AoE，必须是 `Position`，否则位置信息无法传入 Graph。

---

## 验收口径

### 场景 1: 正常施放（命中多目标）

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 D1 键，AimCast 瞄准后确认落点；落点周围 250cm 内有 3 个敌方单位，HP 均为 500，Armor=50 |
| **预期输出** | 3 个目标各扣 `BaseDamage × 1.5 × 100/150` 的 HP；地面圆形范围外单位 HP 不变 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.D1.Damage -> source=<sid> target=<tid> FinalDamage=<v>`（出现 3 次） |
| **截图要求** | 圆形地面指示器半径与实际命中范围一致；3 个伤害浮字同时出现 |
| **多帧录屏** | F0: 按键 → F1: AimCast 指示器出现 → F2: 确认落点 → F3: Search Effect Propose → F4: FanOut Apply → F5: HP 变化 × 3 → F6: 浮字出现 |

### 场景 2: 落点范围内无目标

| 项 | 内容 |
|----|------|
| **输入** | 确认落点，但 250cm 内无敌方单位 |
| **预期输出** | `FanOutApplyEffect` 对空列表执行，无任何 Effect Apply；无浮字、无报错 |
| **Log 关键字** | `[GAS] AoESearch targetCount=0`；无 ApplyEffect 日志 |
| **截图要求** | 地面指示器短暂闪烁后消失，无浮字 |
| **多帧录屏** | F0: 确认落点 → F1: Search Effect（TargetList 为空） → F2: 静止帧，无变化 |

### 场景 3: 资源不足

| 项 | 内容 |
|----|------|
| **输入** | Mana < Cost（施法者法力值不足） |
| **预期输出** | 技能激活失败；AimCast 阶段不进入；UI 显示法力不足提示 |
| **Log 关键字** | `[GAS] AbilityActivationFailed reason=InsufficientMana abilityId=D1` |
| **截图要求** | 技能按钮闪烁红色，无地面指示器出现 |
| **多帧录屏** | F0: 按键 → F0 同帧: 激活失败日志 → 后续帧静止 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/D1CircleAoeTests.cs
[Test]
public void D1_BasicCase_ThreeTargetsInRadius_AllDamaged()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var t1 = SpawnUnit(world, hp: 500, armor: 50, position: new Fix64Vec2(100, 0));
    var t2 = SpawnUnit(world, hp: 500, armor: 50, position: new Fix64Vec2(-50, 80));
    var t3 = SpawnUnit(world, hp: 500, armor: 50, position: new Fix64Vec2(0, -120));
    var outside = SpawnUnit(world, hp: 500, armor: 50, position: new Fix64Vec2(400, 0)); // 范围外

    // Act
    world.SubmitOrder(source, OrderType.CastAbility,
        position: Fix64Vec2.Zero, abilityId: "D1");
    world.Tick(5);

    // Assert
    // FinalDamage = 200 * 1.5 * 100 / (100 + 50) = 200
    Assert.AreEqual(300, GetAttribute(world, t1, "Health"));
    Assert.AreEqual(300, GetAttribute(world, t2, "Health"));
    Assert.AreEqual(300, GetAttribute(world, t3, "Health"));
    Assert.AreEqual(500, GetAttribute(world, outside, "Health")); // 范围外不受影响
}

[Test]
public void D1_EmptyRadius_NoEffectApplied()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    // 无敌方单位在范围内

    world.SubmitOrder(source, OrderType.CastAbility,
        position: Fix64Vec2.Zero, abilityId: "D1");
    world.Tick(5);

    Assert.AreEqual(0, GetApplyEffectCount(world, "Effect.Ability.D1.Damage"));
}

[Test]
public void D1_InsufficientMana_ActivationFailed()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 0, manaCost: 50);

    world.SubmitOrder(source, OrderType.CastAbility,
        position: Fix64Vec2.Zero, abilityId: "D1");
    world.Tick(3);

    Assert.AreEqual(0, GetAbilityActivationCount(world, source, "D1"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "D1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/point_target/d1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/point_target/d1_normal.png`
   - `artifacts/acceptance/point_target/d1_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/point_target/d1_60frames.gif`
   - 标注关键帧：按键帧（F0）、AimCast 指示器帧（F1）、确认帧（F2）、FanOut Apply 帧（F4）、HP 变化帧（F5）、浮字帧（F6）

---

## 参考案例

- **League of Legends 薇古丝 E（黑暗物质）**: 落点圆形 AoE 即时伤害，延迟约 1s 但机制层与此类似（参见 D7 延迟 AoE 变体）
- **Dota 2 虚空假面大招（超时空领域）**: 落点圆形 AoE + 时停 CC，AoE 逻辑结构与 D1 相同，CC 通过 `GrantedTags` 叠加
- **StarCraft II 灵能风暴**: 持续 AoE，每 tick 触发一次 FanOut，参见 D8 变体
