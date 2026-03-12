# Q1: 血量阈值处决（Health Threshold Execute）

> 当目标当前生命值低于设定百分比阈值时，施放者可激活处决技能，造成大额固定伤害或直接击杀。典型案例：Dota 2 Axe Culling Blade（血量 <250 时直接击杀）、LoL Pyke R（阈值内处决并重置冷却）。

---

## 机制描述

玩家对低血量敌方单位按下技能键，系统检查目标当前 HP 与最大 HP 之比是否低于配置阈值（如 20%）。满足条件时激活处决 Effect，造成大量伤害；不满足时订单丢弃或激活失败，不产生任何视觉反馈。核心区别点：前提条件是**目标属性比率门控**，而非施法者资源门控，需要在 AbilityActivation 阶段读取目标属性做比较后才能决定是否允许激活。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Entity`（需要选中敌方单位）
- **InteractionMode**: `TargetFirst`（悬停目标优先）或 `SmartCast`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/q1_health_threshold_execute.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "castModeOverride": null,
  "autoTargetPolicy": "NearestEnemyInRange",
  "autoTargetRangeCm": 600
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/q1_execute.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]                          // 施法者
  LoadContextTarget         E[1]                          // 目标
  // 计算血量比率：ratio = Health / MaxHealth
  LoadAttribute             E[1], Health     → F[0]       // (op 10) 目标当前 HP
  LoadAttribute             E[1], MaxHealth  → F[1]       // (op 10) 目标最大 HP
  DivFloat                  F[0], F[1]       → F[2]       // (op 23) ratio = HP / MaxHP
  // 读取阈值配置
  LoadConfigFloat           "ExecuteThreshold"→ F[3]      // (op 310) 例：0.2
  // 比较：ratio < threshold ⟺ threshold > ratio
  CompareGtFloat            F[3], F[2]       → B[0]       // (op 30) B[0] = threshold > ratio
  JumpIfFalse               B[0], @Abort                  // (op 7) 不满足则跳出
  // 满足条件：写入处决伤害（直接读目标当前 HP 作为伤害量，实现"残血击杀"）
  WriteBlackboardFloat      E[effect], DamageAmount, F[0] // (op 303) 伤害 = 目标当前 HP
  Jump                      @End                          // (op 6)
@Abort:
  ConstFloat                0.0              → F[9]
  WriteBlackboardFloat      E[effect], DamageAmount, F[9] // 写 0，OnApply 不生效
@End:

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  // FinalDamage = DamageAmount（无减伤，处决为真实伤害）
  WriteBlackboardFloat      E[effect], FinalDamage,  F[0]
  WriteBlackboardInt        E[effect], IsTrueDamage,  1   // (op 304) 跳过减伤
  ModifyAttributeAdd        E[effect], E[1], Health, -F[0]// (op 210) 扣血
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Q1.Execute",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "ExecuteThreshold": 0.2
  },
  "grantedTags": [],
  "phaseListeners": []
}
```

> **说明**：阈值门控当前由 Graph 内 `JumpIfFalse` 实现；`AbilityActivationBlockTags.RequiredAll` 已支持正向 Tag 门控。待 Attribute Precondition（P1 需求）落地后，可将数值门控也提前到激活阶段，避免 Effect 进入 OnApply 才被丢弃。

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs` | ✅ 已有 |
| GraphOps (LoadAttribute/DivFloat/CompareGtFloat/JumpIfFalse) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| AbilityActivationAttributePrecondition | `src/Core/Gameplay/GAS/Ability/AbilityActivationPolicy.cs` | ❌ P1 — 需新增目标属性比率门控，当前 OnCalculate JumpIfFalse 可临时代替 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| `AbilityActivationAttributePrecondition`（目标属性门控） | P1 | 目前用 Graph 内 JumpIfFalse 模拟；正式实现需在 AbilityExec 激活检查阶段读取目标属性，支持 ratio/absolute 两种比较模式，失败时输出 `[GAS] AbilityActivationFailed reason=PreconditionFailed` |

---

## 最佳实践

- **DO**: 阈值写入 `Effect.configParams`，不硬编码在 Graph 指令序列里，便于数值调整。
- **DO**: 处决伤害设置 `IsTrueDamage=1`，绕过护甲减伤，保证"低血量击杀"语义正确。
- **DO**: Graph 内用 `DivFloat` 计算比率时注意 `MaxHealth=0` 的边界——`DivFloat` 除零返回 0，比率为 0 会错误触发处决；通过配置 `MaxHealth` 最小值为 1 规避。
- **DON'T**: 不允许在 Graph 内直接将目标 HP 设为 0（结构变更）；必须通过 `ModifyAttributeAdd` 扣减。
- **DON'T**: 不允许在 `OnApply` 阶段重新做阈值判断（与 `OnCalculate` 结果不一致风险）；Gate 逻辑只在 `OnCalculate` 做一次。
- **DON'T**: 不允许 `DoubleTap` InputTriggerType（已废弃）。

---

## 验收口径

### 场景 1: 目标血量满足阈值，处决成功

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 Q 键（`PressedThisFrame`），悬停目标：敌方单位 HP=180, MaxHP=1000（18%）；配置 ExecuteThreshold=0.20 |
| **预期输出** | 目标受到 180 点真实伤害，HP 降为 0；死亡动画触发 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Ability.Q1.Execute -> source=<sid> target=<tid> FinalDamage=180 IsTrueDamage=1` |
| **截图要求** | 处决浮字（特殊颜色）出现在目标头顶，数值=180；目标倒地 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: Effect Propose → F3: OnCalculate（ratio=0.18 < 0.20）→ F4: OnApply → F5: HP=0 → F6: 死亡动画 |

### 场景 2: 目标血量不满足阈值，激活失败

| 项 | 内容 |
|----|------|
| **输入** | 目标 HP=250, MaxHP=1000（25%）；配置 ExecuteThreshold=0.20 |
| **预期输出** | 订单丢弃，无伤害，无浮字；UI 可选显示"目标血量过高"提示 |
| **Log 关键字** | `[GAS] EffectAborted reason=PreconditionFailed phase=OnCalculate effectId=Effect.Ability.Q1.Execute` |
| **截图要求** | 无浮字，目标 HP 条不变 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: Effect Propose → F3: OnCalculate（JumpIfFalse 跳转）→ 后续帧静止 |

### 场景 3: 目标死亡/不可选中

| 项 | 内容 |
|----|------|
| **输入** | 目标 HP ≤ 0 或持有 `Status.Untargetable` Tag |
| **预期输出** | 订单被丢弃；无 Log 报错 |
| **Log 关键字** | `[Input] OrderDiscarded reason=InvalidTarget` |
| **截图要求** | 无浮字，无视觉效果 |
| **多帧录屏** | 仅按键帧，后续帧静止 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/FinisherTests/Q1HealthThresholdExecuteTests.cs
[Test]
public void Q1_TargetBelowThreshold_ExecuteDealsTrueDamage()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 180, maxHp: 1000); // 18% < 20% threshold

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "q1_health_threshold_execute");
    world.Tick(4); // Order → EffectRequest → OnCalculate → OnApply

    // Assert: 180 true damage → HP = 0
    Assert.AreEqual(0, GetAttribute(world, target, "Health"));
}

[Test]
public void Q1_TargetAboveThreshold_EffectAborted()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 250, maxHp: 1000); // 25% > 20% threshold

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "q1_health_threshold_execute");
    world.Tick(4);

    // Assert: HP unchanged
    Assert.AreEqual(250, GetAttribute(world, target, "Health"));
}

[Test]
public void Q1_TargetDead_OrderDiscarded()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 0, maxHp: 1000);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "q1_health_threshold_execute");
    world.Tick(2);

    // Assert: no effect applied, HP stays 0
    Assert.AreEqual(0, GetAttribute(world, target, "Health"));
    Assert.AreEqual(0, GetEffectApplyCount(world, "Effect.Ability.Q1.Execute"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "Q1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/finisher/q1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/finisher/q1_normal.png`（处决成功）
   - `artifacts/acceptance/finisher/q1_edge.png`（阈值边界 HP=200/1000=20% 恰好不满足）
4. 多帧录屏：
   - `artifacts/acceptance/finisher/q1_60frames.gif`
   - 标注关键帧：按键帧、OnCalculate 帧、OnApply 帧、HP 归零帧

---

## 参考案例

- **Dota 2 Axe Culling Blade**: 目标当前 HP < 固定数值时直接击杀，无视护甲
- **LoL Pyke R**: 目标 HP 低于阈值处决，处决时重置冷却并分享金币
- **LoL Urgot R**: 拖拽敌人时若目标血量低于阈值则触发绞杀
