# {ID}: {机制名称}

> 一句话描述 + 典型案例（如：LoL 暗杀者 Q、Dota 死亡先知 W）。

---

## 机制描述
{详细描述：触发条件、核心行为、与其他机制的区别点}

---

## 交互层设计

- **Trigger**: `PressedThisFrame` / `ReleasedThisFrame` / `Held`
- **SelectionType**: `None` / `Entity` / `Position` / `Direction` / `Vector`
- **InteractionMode**: `TargetFirst` / `SmartCast` / `AimCast` / `SmartCastWithIndicator`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/<ability_id>.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",                        // 对应 InputAction ID
  "trigger": "PressedThisFrame",                  // InputTriggerType
  "orderTypeKey": "castAbility",                  // 对应 OrderTypeRegistry 中的 key
  "selectionType": "Entity",                      // OrderSelectionType
  "isSkillMapping": true,                         // 受 InteractionMode 影响
  "castModeOverride": null,                       // null = 跟随全局 InteractionMode
  "autoTargetPolicy": "NearestEnemyInRange",      // 可选，SmartCast 无悬停目标时的回退策略
  "autoTargetRangeCm": 600
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/<effect_id>.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]          // 施法者
  LoadContextTarget         E[1]          // 目标
  LoadAttribute             E[0], BaseDamage → F[0]
  LoadConfigFloat           "DamageCoeff" → F[1]
  MulFloat                  F[0], F[1]   → F[2]
  WriteBlackboardFloat      E[effect], DamageAmount, F[2]

Phase OnApply Listener (priority=200, scope=Target):  // 护甲减伤
  ReadBlackboardFloat       E[effect], DamageAmount → F[0]
  LoadAttribute             E[1], Armor             → F[1]
  // FinalDamage = DamageAmount * 100 / (100 + Armor)
  ...
  WriteBlackboardFloat      E[effect], FinalDamage, F[result]

Phase OnApply Main:
  ReadBlackboardFloat       E[effect], FinalDamage → F[0]
  ModifyAttributeAdd        E[effect], E[1], Health, -F[0]
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.{ID}.Main",
  "presetType": "InstantDamage",            // EffectPresetType
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 1.5
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
| {NewComponent} | `src/Core/Gameplay/GAS/...` | ❌ P1 — 需新增 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| {需求描述} | P1 | {详细说明：缺什么、为什么缺、影响范围} |

> 若无缺口，写：无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 伤害公式走 `OnCalculate` Graph + Blackboard，不在 `OnApply` 里硬编码数值。
- **DO**: 结构变更（创建实体、挂组件）统一走 `RuntimeEntitySpawnQueue`，不在 Effect Handler 里直接调 `World.Create`。
- **DO**: 跨层写入统一用 `AttributeSink`（如 float→Fix64 的物理层边界）。
- **DON'T**: 不允许在 Graph 内做结构变更（只能读写 Blackboard、属性、发布 Effect）。
- **DON'T**: 不允许 `DoubleTap` InputTriggerType（已废弃，双击属于 SelectionSystem）。
- **DON'T**: CC Tag（Status.Stunned 等）不能由 Graph 直接写位，必须通过 `GrantedTags` + Effect 生命周期管理。

---

## 验收口径

### 场景 1: 正常施放路径

| 项 | 内容 |
|----|------|
| **输入** | 玩家按 Q 键（`PressedThisFrame`），悬停目标：敌方单位 HP=500, Armor=50 |
| **预期输出** | `FinalDamage = BaseDamage × Coeff × 100/(100+Armor)`；目标 HP 对应扣减 |
| **Log 关键字** | `[GAS] ApplyEffect {effect_id} -> source=<sid> target=<tid> FinalDamage=<v>` |
| **截图要求** | 伤害浮字出现在目标头顶，数值与计算结果一致 |
| **多帧录屏** | F0: 按键 → F1: Order 入队 → F2: Effect Propose → F4: OnApply → F5: HP 变化 → F6: 浮字出现 |

### 场景 2: 目标无效（死亡/不可选中）

| 项 | 内容 |
|----|------|
| **输入** | 目标 HP ≤ 0 或持有 `Status.Untargetable` Tag |
| **预期输出** | 订单被丢弃或 Effect 不 Apply；无 Log 报错 |
| **Log 关键字** | `[Input] OrderDiscarded reason=InvalidTarget` |
| **截图要求** | 无浮字，无视觉效果 |
| **多帧录屏** | 仅按键帧，后续帧静止 |

### 场景 3: 资源不足

| 项 | 内容 |
|----|------|
| **输入** | Mana < Cost |
| **预期输出** | 激活失败；UI 提示法力不足 |
| **Log 关键字** | `[GAS] AbilityActivationFailed reason=InsufficientMana` |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/{MechanismName}Tests.cs
[Test]
public void {ID}_BasicCase_CorrectDamageApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200);
    var target = SpawnUnit(world, hp: 500, armor: 50);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, target, abilityId: "{ability_id}");
    world.Tick(3);  // Order → EffectRequest → Apply

    // Assert
    // FinalDamage = 200 * 1.5 * 100/(100+50) = 200
    Assert.AreEqual(300, GetAttribute(world, target, "Health"));
}

[Test]
public void {ID}_TargetDead_EffectNotApplied()
{
    // target HP = 0 → assert HP unchanged
}

[Test]
public void {ID}_InsufficientMana_ActivationFailed()
{
    // Mana = 0, Cost = 50 → assert activation count = 0
}
```

### 集成验收
1. 运行 `dotnet test --filter "{ID}"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/{mechanism_dir}/{id}_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/{mechanism_dir}/{id}_normal.png`
   - `artifacts/acceptance/{mechanism_dir}/{id}_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/{mechanism_dir}/{id}_60frames.gif` / `.mp4`
   - 标注关键帧：按键帧、Effect Apply 帧、结果帧

---

## 参考案例

- **{游戏名} {英雄名} {技能名}**: {简述机制要点}
