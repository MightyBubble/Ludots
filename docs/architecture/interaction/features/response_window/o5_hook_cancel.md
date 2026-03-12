# O5: Hook/取消

> 在效果结算前，Phase Listener 自动检测并设置 SkipMain 标志位，阻止效果主体执行（如：Dota Linken's Sphere 自动封锁、LoL Banshee's Veil）。

---

## 机制描述

Hook/取消是一种程序自动取消效果的机制，无需玩家交互。核心特征：
- **自动执行**：Phase Listener 在 OnPropose 阶段自动触发
- **无玩家交互**：与 O1 不同，无需弹出 UI 提示
- **条件判断**：通过 Graph 条件决定是否取消

与其他机制的区别：
- vs O1 陷阱/反制：O1 有玩家选择步骤，O5 是全自动
- vs O6 Modify/修改：O6 修改数值，O5 完全阻止执行
- vs O8 超时：O8 是等待超时后的行为，O5 是即时取消

---

## 交互层设计

- **Trigger**: 无（Phase Listener 自动触发）
- **SelectionType**: `None`
- **InteractionMode**: 不适用

```json5
// 配置路径: mods/<yourMod>/Effects/<shield_effect_id>.json
// Phase Listener 挂在护盾 Effect 上
// 接口定义: src/Core/Gameplay/GAS/EffectTemplateRegistry.cs
{
  "id": "Effect.SpellShield.Active",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "ticks": -1 },
  "grantedTags": ["Status.SpellShielded"],
  "phaseListeners": [
    {
      "phase": "OnPropose",           // 在 Propose 阶段拦截
      "priority": 10,                 // 优先级低（最先执行），确保在伤害计算前取消
      "scope": "Target",
      "graphId": "Graph.Hook.SpellShield"
    }
  ]
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect Phase 执行顺序: OnPropose → OnCalculate → OnResolve → OnHit → OnApply → ...
// Phase Listener 在主体之前执行（priority 越小越先）

// Phase OnPropose Listener (priority=10, scope=Target)
Graph.Hook.SpellShield:
  LoadContextTarget         E[1]          // 被攻击单位
  LoadContextSource         E[0]          // 攻击者

  // 检查护盾条件：目标持有护盾 Tag
  HasTag                    E[1], "Status.SpellShielded" → B[0]
  JumpIfFalse               B[0], @no_shield

  // 检查效果类型：仅对单体指向技能有效
  // 通过 Blackboard 读取效果标签（已在 EffectContext 中设置）
  ReadBlackboardInt         E[effect], "IsSingleTargetSpell" → I[0]
  ConstInt                  1             → I[1]
  CompareEqInt              I[0], I[1]    → B[1]
  JumpIfFalse               B[1], @no_shield

  // 取消效果
  WriteBlackboardInt        E[effect], "SkipMain", 1

  // 消耗护盾（护盾只能使用一次）
  // 注意：结构变更（移除 Tag）需通过 ApplyEffectTemplate 完成
  LoadConfigInt             "ConsumeShieldEffectId" → I[2]
  ApplyEffectDynamic        E[1], E[1], I[2]   // 移除护盾 Tag 的 Effect

  Jump                      @end

@no_shield:
  // 不满足条件，不做任何事
@end:
```

护盾消耗 Effect 模板：
```json5
{
  "id": "Effect.SpellShield.Consume",
  "presetType": "InstantRemoveBuff",
  "grantedTags": [],
  "removedTags": ["Status.SpellShielded"],  // 移除护盾 Tag
  "lifetime": { "kind": "Instant" }
}
```

被动 Hook（SkipMain 设置详解）：
```
Effect Phase 执行顺序（每个 Phase 内）：
  Pre → Main → Post → Listeners（按 priority 升序）

OnPropose 阶段：
  Main（EffectProposalProcessingSystem 默认处理）
  Listener priority=10 (SpellShield Hook) ← 先执行
  Listener priority=50 (其他 Listener)
  ...

SkipMain 标志检查：
  EffectPhaseExecutor 在进入 OnCalculate 前检查 Blackboard["SkipMain"]
  若 = 1，跳过后续所有 Phase（OnCalculate, OnApply 等）
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| ResponseChainListener (Phase Listener) | `src/Core/Gameplay/GAS/Components/ResponseChainListener.cs` | ✅ 已有 |
| BlackboardIntBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| GraphOps (HasTag, WriteBlackboardInt, ApplyEffectDynamic) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: Phase Listener 的 priority 设为小于 50（如 10），确保在其他 Listener 之前执行检查。
- **DO**: SkipMain=1 写入后，不要再执行其他副作用（如扣除资源），避免状态不一致。
- **DO**: 护盾消耗通过独立的 `ApplyEffectDynamic` 完成，不要在 Hook Graph 内直接修改属性。
- **DON'T**: 不要用 Hook 取消 AoE 效果（AoE 效果的目标不是单体，Hook 仅对 OnPropose 阶段的目标有效）。
- **DON'T**: 不要在 OnApply Listener 中设置 SkipMain（为时已晚，OnApply 阶段已执行）。
- **DON'T**: CC Tag（Status.Stunned 等）不能由 Graph 直接写位，必须通过 GrantedTags + Effect 生命周期管理。

---

## 验收口径

### 场景 1: 法术护盾自动拦截

| 项 | 内容 |
|----|------|
| **输入** | 单位持有 `Status.SpellShielded`；敌方施放单体法术（`IsSingleTargetSpell=1`） |
| **预期输出** | `SkipMain=1` 写入；法术不造成伤害；护盾被消耗（`Status.SpellShielded` 移除） |
| **Log 关键字** | `[GAS] PhaseListener OnPropose priority=10 SpellShield` → `[GAS] SkipMain=1 effectId=<id>` → `[GAS] ApplyEffect ConsumeShield target=<defender>` |
| **截图要求** | 护盾破碎动画播放，无伤害浮字，护盾量条消失 |
| **多帧录屏** | F0: 法术提议 → F1: OnPropose Listener 执行 → F2: SkipMain=1 → F3: 护盾消耗 → F4: OnCalculate 跳过 |

### 场景 2: 不满足条件（AoE 不触发）

| 项 | 内容 |
|----|------|
| **输入** | 单位持有 `Status.SpellShielded`；敌方施放 AoE 法术（`IsSingleTargetSpell=0`） |
| **预期输出** | Hook 不触发，AoE 法术正常造成伤害，护盾不被消耗 |
| **Log 关键字** | `[GAS] PhaseListener OnPropose priority=10 SpellShield → ConditionFailed IsSingleTargetSpell=0` |
| **截图要求** | 护盾量条保留，目标受到 AoE 伤害 |
| **多帧录屏** | F0: AoE 提议 → F1: Listener 检查 → F2: 条件失败 → F3: 正常 OnApply |

### 场景 3: 护盾已消耗（状态不满足）

| 项 | 内容 |
|----|------|
| **输入** | 护盾被消耗后（`Status.SpellShielded` 已移除），再次受到单体法术攻击 |
| **预期输出** | Hook 不触发，法术正常造成伤害 |
| **Log 关键字** | `[GAS] PhaseListener OnPropose priority=10 SpellShield → ConditionFailed NoShieldTag` |
| **截图要求** | 无护盾破碎动画，正常伤害浮字 |
| **多帧录屏** | F0: 法术提议 → F1: 无护盾检查失败 → F2: 正常结算 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ResponseWindowTests.cs
[Test]
public void O5_HookCancel_SpellShield_BlocksSingleTargetSpell()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500);

    // 为 defender 附加法术护盾 Effect
    ApplyEffect(world, "Effect.SpellShield.Active", source: defender, target: defender);
    Assert.IsTrue(HasTag(world, defender, "Status.SpellShielded"));

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.Fireball");
    world.Tick(3);  // OnPropose → Hook → SkipMain → 跳过结算

    // Assert
    Assert.AreEqual(500f, GetAttribute(world, defender, "Health")); // 不扣血
    Assert.IsFalse(HasTag(world, defender, "Status.SpellShielded")); // 护盾被消耗
}

[Test]
public void O5_HookCancel_AoENotBlocked_DamageDealt()
{
    // defender 有护盾，AoE 不触发 Hook
    // Assert: defender HP < 500, shield tag still present
}

[Test]
public void O5_HookCancel_ShieldConsumed_SecondSpellHits()
{
    // 护盾消耗后，第二次法术正常命中
    // Assert: defender HP < 500 after second spell
}
```

### 集成验收
1. 运行 `dotnet test --filter "O5_HookCancel"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o5_hook_cancel_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o5_shield_blocks.png`
   - `artifacts/acceptance/response_window/o5_aoe_not_blocked.png`
   - `artifacts/acceptance/response_window/o5_shield_consumed.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o5_60frames.mp4`
   - 标注关键帧：提议帧、Listener 执行帧、SkipMain 写入帧、结算跳过帧

---

## 参考案例

- **Dota Linken's Sphere**: 自动拦截单体指向技能（无 UI 交互，完全自动）。
- **LoL Banshee's Veil**: 每 N 秒自动拦截一次技能，无需玩家操作。
- **LoL Sivir E**: 手动激活（O1 类型），与 Banshee 被动自动（O5 类型）形成对比。
