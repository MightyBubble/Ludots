# O7: Chain/追加效果

> Phase Listener 在 OnApply 阶段自动追加新效果，形成效果链（如：Dota Templar Assassin Refraction 消耗层数、LoL 被动触发额外伤害）。

---

## 机制描述

Chain/追加效果允许 Phase Listener 在效果结算的 OnApply 阶段调用 `ApplyEffectTemplate`，创建新的效果实例。核心特征：
- **Phase 时机**：在 OnApply 阶段执行（原效果已应用后）
- **新效果创建**：通过 `ApplyEffectTemplate` 或 `ApplyEffectDynamic` 创建新 EffectRequest
- **条件触发**：通过 Graph 条件判断是否追加（如检查层数、冷却）

与其他机制的区别：
- vs O2 连锁响应：O2 是多个 Listener 对同一效果的响应，O7 是单个 Listener 追加新效果
- vs O6 Modify/修改：O6 修改现有效果数值，O7 创建全新效果

---

## 交互层设计

- **Trigger**: 无（Phase Listener 自动触发）
- **SelectionType**: `None`
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/Effects/<refraction_effect_id>.json
// Phase Listener 挂在 Refraction Buff Effect 上
// 接口定义: src/Core/Gameplay/GAS/EffectTemplateRegistry.cs
{
  "id": "Effect.Refraction.Active",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "ticks": 600 },
  "grantedTags": ["Status.Refracted"],
  "phaseListeners": [
    {
      "phase": "OnApply",        // 在伤害应用后追加
      "priority": 200,           // 在主体之后执行
      "scope": "Target",
      "graphId": "Graph.Chain.ConsumeRefraction"
    }
  ],
  "configParams": {
    "MaxCharges": 6,
    "BlockDamageEffectId": 2001
  }
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Phase 执行顺序: OnPropose → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove
// OnApply 内部: Pre → Main → Post → Listeners（按 priority 升序）

// Phase OnApply Listener (priority=200, scope=Target)
Graph.Chain.ConsumeRefraction:
  LoadContextTarget         E[1]                       // 被攻击单位（持有 Refraction）

  // 检查是否有剩余层数
  LoadAttribute             E[1], RefractionCharges  → F[0]
  ConstFloat                0.0                      → F[1]
  CompareGtFloat            F[0], F[1]               → B[0]
  JumpIfFalse               B[0], @no_charges

  // 消耗一层
  ConstFloat                -1.0                     → F[2]
  ModifyAttributeAdd        E[effect], E[1], RefractionCharges, F[2]

  // 追加格挡伤害效果（回溯性取消伤害）
  LoadConfigInt             "BlockDamageEffectId"   → I[0]
  ApplyEffectDynamic        E[1], E[1], I[0]         // 对自己施加格挡效果

  // 检查层数是否耗尽
  LoadAttribute             E[1], RefractionCharges → F[3]
  ConstFloat                0.0                     → F[4]
  CompareGtFloat            F[3], F[4]              → B[1]
  JumpIfFalse               B[1], @remove_buff

  Jump                      @end

@remove_buff:
  // 层数耗尽，移除 Buff
  LoadConfigInt             "RemoveRefractionEffectId" → I[1]
  ApplyEffectDynamic        E[1], E[1], I[1]

  Jump                      @end

@no_charges:
  // 无层数，不做任何事
@end:
```

格挡伤害 Effect 模板（回溯性取消伤害）：
```json5
{
  "id": "Effect.Refraction.BlockDamage",
  "presetType": "InstantHeal",
  "lifetime": { "kind": "Instant" },
  "phaseListeners": [
    {
      "phase": "OnApply",
      "priority": 10,
      "scope": "Target",
      "graphId": "Graph.Refraction.RestoreHealth"
    }
  ]
}

// Graph.Refraction.RestoreHealth:
// 读取上一次伤害的 Blackboard["DamageAmount"]，回复等量 HP
Phase OnApply Main:
  LoadContextTarget         E[1]
  ReadBlackboardFloat       E[effect], "LastDamageAmount" → F[0]
  ModifyAttributeAdd        E[effect], E[1], Health, F[0]  // 回复 HP
```

被动触发额外伤害示例（LoL 式）：
```
// Phase OnApply Listener (priority=200, scope=Source) — 攻击者触发
Graph.Chain.OnHitBonus:
  LoadContextSource         E[0]                       // 攻击者
  LoadContextTarget         E[1]                       // 被攻击者

  // 检查冷却
  LoadAttribute             E[0], OnHitBonusCooldown → F[0]
  ConstFloat                0.0                      → F[1]
  CompareGtFloat            F[0], F[1]               → B[0]
  JumpIfFalse               B[0], @on_cooldown

  // 追加额外伤害效果
  LoadConfigInt             "BonusDamageEffectId"   → I[0]
  ApplyEffectDynamic        E[0], E[1], I[0]

  // 设置冷却
  LoadConfigFloat           "CooldownTicks"         → F[2]
  ModifyAttributeAdd        E[effect], E[0], OnHitBonusCooldown, F[2]

  Jump                      @end

@on_cooldown:
  // 冷却中，不触发
@end:
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GraphOps (ApplyEffectTemplate, ApplyEffectDynamic) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| GraphOps (ModifyAttributeAdd) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: Phase Listener 的 priority 设为大于 100（如 200），确保在 OnApply Main 之后执行。
- **DO**: 使用 `ApplyEffectDynamic` 而非 `ApplyEffectTemplate`，避免硬编码 target。
- **DO**: 在 Graph 中检查条件（层数、冷却、Tag），避免无效追加。
- **DON'T**: 不要在 OnPropose 或 OnCalculate 阶段追加效果（时机过早，原效果可能被取消）。
- **DON'T**: 不要创建循环依赖（A 追加 B，B 追加 A），会导致无限递归。
- **DON'T**: 不要在 Graph 内直接修改 Health 属性（通过 ApplyEffectDynamic 创建 Heal/Damage Effect）。

---

## 验收口径

### 场景 1: Refraction 消耗层数格挡伤害

| 项 | 内容 |
|----|------|
| **输入** | 单位持有 Refraction（RefractionCharges=3）；受到伤害 100 |
| **预期输出** | 伤害应用后，OnApply Listener 触发，消耗 1 层，追加格挡效果，HP 回复 100；剩余层数 2 |
| **Log 关键字** | `[GAS] OnApply Main Health -100` → `[GAS] PhaseListener OnApply priority=200 ConsumeRefraction` → `[GAS] ModifyAttributeAdd RefractionCharges -1` → `[GAS] ApplyEffect BlockDamage` → `[GAS] Health +100` |
| **截图要求** | HP 条先减少后回复，Refraction 层数显示 2 |
| **多帧录屏** | F0: 伤害提议 → F1: OnApply Main → F2: HP -100 → F3: Listener 触发 → F4: 格挡效果 → F5: HP +100 |

### 场景 2: 层数耗尽移除 Buff

| 项 | 内容 |
|----|------|
| **输入** | 单位持有 Refraction（RefractionCharges=1）；受到伤害 |
| **预期输出** | 消耗最后 1 层后，Buff 被移除，`Status.Refracted` Tag 消失 |
| **Log 关键字** | `[GAS] RefractionCharges: 1 → 0` → `[GAS] ApplyEffect RemoveRefraction` → `[GAS] Tag Removed Status.Refracted` |
| **截图要求** | Refraction 图标消失 |
| **多帧录屏** | F0: 伤害 → F1: 消耗层数 → F2: Buff 移除 |

### 场景 3: 被动触发额外伤害（冷却中不触发）

| 项 | 内容 |
|----|------|
| **输入** | 攻击者有被动（OnHitBonusCooldown=0）；攻击目标；冷却设置为 60 ticks；再次攻击 |
| **预期输出** | 第一次攻击触发额外伤害，冷却开始；第二次攻击不触发 |
| **Log 关键字** | `[GAS] OnHitBonus triggered` → `[GAS] OnHitBonusCooldown=60` → `[GAS] OnHitBonus on_cooldown` |
| **截图要求** | 第一次攻击有额外伤害浮字，第二次无 |
| **多帧录屏** | F0: 第一次攻击 → F1: 额外伤害 → F60: 第二次攻击 → F61: 无额外伤害 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O7_ChainAppend_Refraction_BlocksDamageAndConsumesCharge()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500);

    // 为 defender 附加 Refraction Buff（3 层）
    ApplyEffect(world, "Effect.Refraction.Active", source: defender, target: defender);
    SetAttribute(world, defender, "RefractionCharges", 3f);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.BasicAttack");
    world.Tick(5);  // OnApply → Listener → BlockDamage

    // Assert
    Assert.AreEqual(500f, GetAttribute(world, defender, "Health")); // HP 不变（格挡）
    Assert.AreEqual(2f, GetAttribute(world, defender, "RefractionCharges")); // 消耗 1 层
}

[Test]
public void O7_ChainAppend_Refraction_RemovedWhenChargesZero()
{
    // RefractionCharges=1 → 消耗后 Buff 移除
    // Assert: !HasTag(defender, "Status.Refracted")
}

[Test]
public void O7_ChainAppend_OnHitBonus_CooldownPreventsRetrigger()
{
    // 第一次攻击触发，冷却 60 ticks
    // 第二次攻击（tick < 60）不触发
    // Assert: bonus damage applied once only
}
```

### 集成验收
1. 运行 `dotnet test --filter "O7_ChainAppend"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o7_chain_append_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o7_refraction_block.png`
   - `artifacts/acceptance/response_window/o7_charges_depleted.png`
   - `artifacts/acceptance/response_window/o7_onhit_cooldown.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o7_60frames.mp4`
   - 标注关键帧：OnApply Main 帧、Listener 触发帧、追加效果帧

---

## 参考案例

- **Dota Templar Assassin Refraction**: 吸收固定次数伤害，每次消耗一层。
- **LoL 被动触发额外伤害**: 如 Vayne W（每三次攻击触发真实伤害）。
- **Dota Blade Mail**: 受到伤害时反弹等量伤害（OnApply Listener 追加反伤效果）。
