# O6: 修改伤害值

> Phase Listener 在 OnCalculate 阶段拦截效果，通过修改 Blackboard 中的 FinalDamage 值来减少或增加最终伤害（如：Dota Bristleback 背刺减伤、护盾吸收、护甲减伤公式）。

---

## 机制描述

修改伤害值允许 Phase Listener 在效果结算的 OnCalculate 阶段介入，读取并修改 Blackboard 中的伤害相关键值。核心特征：
- **Phase 时机**：在 OnCalculate 阶段执行（伤害计算后、实际应用前）
- **Blackboard 中转**：通过标准 Blackboard Key（DamageAmount、FinalDamage）传递修改后的数值
- **优先级叠加**：多个 Listener 按 priority 升序执行，乘法叠加

与其他机制的区别：
- vs O5 Hook/取消：O5 完全阻止，O6 修改数值后继续结算
- vs O7 Chain/追加：O7 追加新效果，O6 修改现有效果的数值

---

## 交互层设计

- **Trigger**: 无（Phase Listener 自动触发）
- **SelectionType**: `None`
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/Effects/<armor_effect_id>.json
// Phase Listener 挂在护甲/减伤 Buff Effect 上
// 接口定义: src/Core/Gameplay/GAS/EffectTemplateRegistry.cs
{
  "id": "Effect.ArmorPassive.Active",
  "presetType": "Buff",
  "lifetime": { "kind": "Permanent" },
  "phaseListeners": [
    {
      "phase": "OnCalculate",    // 伤害计算阶段
      "priority": 100,           // 护甲在护盾之后（priority=100 后于 priority=50）
      "scope": "Target",
      "graphId": "Graph.Modify.ArmorReduction"
    }
  ]
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// 标准 Blackboard Key: DamageAmount(float), FinalDamage(float)
// Phase 执行顺序: OnPropose → OnCalculate → OnResolve → OnHit → OnApply
// OnCalculate 内部: Pre → Main → Post → Listeners（按 priority 升序）

// Phase OnCalculate Listener (priority=50, scope=Target) — 护盾优先吸收（平减）
Graph.Modify.ShieldAbsorption:
  LoadContextTarget         E[1]                       // 被攻击单位
  ReadBlackboardFloat       E[effect], DamageAmount  → F[0]   // 读取原始伤害
  LoadAttribute             E[1], ShieldAmount        → F[1]   // 读取护盾量

  // 实际减伤 = min(伤害, 护盾量)
  MinFloat                  F[0], F[1]                 → F[2]
  SubFloat                  F[0], F[2]                 → F[3]   // 剩余伤害

  WriteBlackboardFloat      E[effect], DamageAmount, F[3]       // 更新伤害
  WriteBlackboardFloat      E[effect], MitigatedAmount, F[2]    // 记录吸收量

  // 消耗护盾
  NegFloat                  F[2]                       → F[4]
  ModifyAttributeAdd        E[effect], E[1], ShieldAmount, F[4]  // 减少护盾

// Phase OnCalculate Listener (priority=100, scope=Target) — 护甲百分比减伤（乘法）
Graph.Modify.ArmorReduction:
  LoadContextTarget         E[1]                       // 被攻击单位
  ReadBlackboardFloat       E[effect], DamageAmount  → F[0]   // 读取（已被护盾修改的）伤害

  // Dota 式护甲公式: FinalDamage = DamageAmount * 100 / (100 + Armor)
  LoadAttribute             E[1], Armor               → F[1]
  ConstFloat                100.0                     → F[2]
  AddFloat                  F[2], F[1]                → F[3]   // 100 + Armor
  DivFloat                  F[2], F[3]                → F[4]   // 100 / (100 + Armor)
  MulFloat                  F[0], F[4]                → F[5]   // 最终伤害

  WriteBlackboardFloat      E[effect], DamageAmount, F[5]       // 更新伤害

// Phase OnApply Main — 最终应用伤害
Phase OnApply Main:
  ReadBlackboardFloat       E[effect], DamageAmount  → F[0]
  NegFloat                  F[0]                     → F[1]   // 负数（扣血）
  ModifyAttributeAdd        E[effect], E[1], Health, F[1]      // 扣除 HP
```

多个 Modifier 的执行顺序与叠加说明：
```
OnCalculate Listeners 执行顺序（priority 升序）：
  priority=50:  护盾吸收（平减）  DamageAmount: 100 → 80（护盾吸收 20）
  priority=100: 护甲减伤（乘法）  DamageAmount: 80 → 57（护甲 40，公式：80 * 100/140 ≈ 57）

最终 OnApply 读取 DamageAmount=57，目标扣血 57。
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| GraphOps (MulFloat, DivFloat, MinFloat, SubFloat, NegFloat) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps (WriteBlackboardFloat, ReadBlackboardFloat) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps (ModifyAttributeAdd) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 平减（护盾吸收）使用低 priority（如 50），百分比减伤（护甲）使用高 priority（如 100），确保正确的叠加顺序。
- **DO**: 修改后的数值写回 `DamageAmount` Blackboard Key，而非写入新 Key，确保后续 Listener 读取到最新值。
- **DO**: 记录每个 Modifier 的减伤量（写入 `MitigatedAmount`），便于统计和 UI 显示。
- **DON'T**: 不要在 OnApply Listener 中修改 DamageAmount（OnApply 是执行阶段，修改无效）。
- **DON'T**: 不要在 Graph 内直接调用 `ModifyAttributeAdd` 修改 Health（只修改 Blackboard，由 OnApply Main 执行实际扣血）。
- **DON'T**: 不要允许 DamageAmount 变为负数（使用 `MaxFloat 0.0` 限制）。

---

## 验收口径

### 场景 1: 护甲减伤（百分比）

| 项 | 内容 |
|----|------|
| **输入** | 单位持有 Armor=100；受到原始伤害 DamageAmount=200 |
| **预期输出** | `FinalDamage = 200 * 100 / (100+100) = 100`；HP 扣减 100 |
| **Log 关键字** | `[GAS] PhaseListener OnCalculate priority=100 ArmorReduction` → `[GAS] DamageAmount: 200 → 100` → `[GAS] ModifyAttributeAdd Health -100` |
| **截图要求** | 伤害浮字显示 100，HP 条减少相应量 |
| **多帧录屏** | F0: 伤害提议 → F1: OnCalculate Listener → F2: DamageAmount 修改 → F3: OnApply → F4: HP 变化 |

### 场景 2: 护盾 + 护甲叠加（先平减后乘法）

| 项 | 内容 |
|----|------|
| **输入** | 单位持有护盾（ShieldAmount=20）和护甲（Armor=40）；受到伤害 DamageAmount=100 |
| **预期输出** | 护盾吸收后：100-20=80；护甲减伤：80×100/140≈57；HP 扣减 57 |
| **Log 关键字** | `[GAS] ShieldAbsorption: 100 → 80 (absorbed=20)` → `[GAS] ArmorReduction: 80 → 57` → `[GAS] ApplyDamage=57` |
| **截图要求** | 护盾量条减少，伤害浮字显示 57 |
| **多帧录屏** | F0: 伤害提议 → F1: priority=50 护盾 Listener → F2: priority=100 护甲 Listener → F3: OnApply |

### 场景 3: 真实伤害（跳过所有减伤）

| 项 | 内容 |
|----|------|
| **输入** | 效果标记 `IsTrueDamage=1`；单位有护甲（Armor=100） |
| **预期输出** | 所有减伤 Listener 跳过，HP 直接扣减原始伤害 |
| **Log 关键字** | `[GAS] PhaseListener OnCalculate ArmorReduction → SkippedTrueDamage` |
| **截图要求** | 伤害浮字带有特殊标记（如不同颜色） |
| **多帧录屏** | F0: 真实伤害提议 → F1: Listener 跳过 → F2: 直接 OnApply |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O6_ModifyValue_ArmorReduction_CorrectFormula()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 200);
    var defender = SpawnUnit(world, hp: 1000, armor: 100);

    // 为 defender 附加护甲减伤 Listener（挂在被动 Effect 上）
    ApplyEffect(world, "Effect.ArmorPassive.Active", source: defender, target: defender);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.BasicAttack");
    world.Tick(3);

    // Assert: FinalDamage = 200 * 100 / (100+100) = 100
    Assert.AreEqual(900f, GetAttribute(world, defender, "Health"));
}

[Test]
public void O6_ModifyValue_ShieldPlusArmor_CorrectStacking()
{
    // ShieldAmount=20, Armor=40, DamageAmount=100
    // Assert: HP = 500 - 57 = 443 (approximate due to Fix64)
}

[Test]
public void O6_ModifyValue_TrueDamage_BypassesModifiers()
{
    // IsTrueDamage=1, Armor=100, DamageAmount=200
    // Assert: HP = 800 (扣减原始 200)
}
```

### 集成验收
1. 运行 `dotnet test --filter "O6_ModifyValue"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o6_modify_value_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o6_armor_reduction.png`
   - `artifacts/acceptance/response_window/o6_shield_armor_stack.png`
   - `artifacts/acceptance/response_window/o6_true_damage.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o6_60frames.mp4`
   - 标注关键帧：OnCalculate 帧、Blackboard 修改帧、OnApply 帧

---

## 参考案例

- **Dota Bristleback 背刺减伤**: 根据攻击角度自动减伤（OnCalculate Listener 读取角度属性）。
- **LoL 护甲减伤公式**: 类 Dota 护甲公式，多个减伤层叠加。
- **Dota Hood of Defiance**: 固定百分比魔法减伤，OnCalculate Listener。
