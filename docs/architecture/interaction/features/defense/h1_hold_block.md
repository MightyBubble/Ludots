# H1: 持续格挡（Hold Block）

> 按住按键期间持续降低承受伤害。典型案例：《黑暗之魂》盾反前摇、《战神》L1格挡、《守望先锋》莱因哈特盾墙。

---

## 机制描述

玩家按住指定按键时激活格挡姿态，持续施加减伤 Buff；松开按键立即解除。不同于弹反（H2）的瞬时窗口，本机制无时间限制，依赖资源（体力/耐久）约束。格挡期间可叠加"格挡值"损耗；超过阈值后触发硬直/破盾。

---

## 交互层设计

- **Trigger**: `Held`，配合 `HeldPolicy = StartEnd`（Down 触发 block.Start，Up 触发 block.End）
- **SelectionType**: `None`
- **InteractionMode**: `SmartCast`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/hold_block.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Block",
  "trigger": "Held",
  "heldPolicy": "StartEnd",          // Down→block.Start；Up→block.End
  "orderTypeKey": "castAbility",
  "selectionType": "None",
  "isSkillMapping": true,
  "castModeOverride": "SmartCast"
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:   mods/<yourMod>/Effects/hold_block_buff.json
// 注册中心:      src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

=== block.Start（按下 → 施加持续 Buff）===

Phase OnApply Main:
  LoadContextSource          E[0]          // 施法者
  ApplyEffectTemplate        E[0], "Effect.HoldBlock.BlockingBuff"
  // BlockingBuff 是 Duration=∞ 的 Buff，携带 blocking Tag + 减伤 Listener

=== block.End（松开 → 移除 Buff）===

Phase OnApply Main:
  LoadContextSource          E[0]
  // 通过 RemoveByGrantedTag 机制移除 BlockingBuff
  // (Effect 生命周期由 HasTag("blocking") 驱动，松开后 ability 发出 RemoveEffect 信号)

=== BlockingBuff 内置 Listener（减伤）===

// Effect: Effect.HoldBlock.BlockingBuff
// presetType: Custom, lifetime: { kind: "UntilRemoved" }
// grantedTags: ["Status.Blocking"]

Phase OnApply Listener (eventTagId="incoming_damage", priority=100, scope=Self):
  // 当持有 blocking Tag 时，拦截伤害并乘以减伤系数
  ReadBlackboardFloat        E[effect], DamageAmount → F[0]
  LoadConfigFloat            "MitigationCoeff"       → F[1]   // 如 0.15 = 85%减伤
  MulFloat                   F[0], F[1]              → F[2]
  WriteBlackboardFloat       E[effect], FinalDamage,   F[2]
  // 同步扣减格挡耐久（Guard Stamina 属性）
  LoadConfigFloat            "StaminaDrainPerHit"    → F[3]
  NegFloat                   F[3]                    → F[4]
  LoadContextTarget          E[1]
  ModifyAttributeAdd         E[effect], E[1], GuardStamina, F[4]
```

Effect 模板示例：
```json5
{
  "id": "Effect.HoldBlock.BlockingBuff",
  "presetType": "Custom",
  "lifetime": { "kind": "UntilRemoved" },
  "configParams": {
    "MitigationCoeff": 0.15,
    "StaminaDrainPerHit": 10.0
  },
  "grantedTags": ["Status.Blocking"],
  "phaseListeners": [
    {
      "phase": "OnApply",
      "eventTagId": "incoming_damage",
      "priority": 100,
      "scope": "Self",
      "graphProgramId": "Graph.HoldBlock.MitigationListener"
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
| Held + HeldPolicy=StartEnd | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| GrantedTags 生命周期管理 | `src/Core/Gameplay/GAS/Systems/EffectTagSystem.cs` | ✅ 已有 |
| GuardStamina 属性 | `src/Core/Gameplay/GAS/Components/AttributeSet.cs` | ❌ P1 — 需在 AttributeSet 中注册 GuardStamina 属性 |
| 破盾检测系统 | `src/Core/Gameplay/GAS/Systems/GuardBreakSystem.cs` | ❌ P1 — 需新增：监听 GuardStamina≤0 → 强制移除 BlockingBuff + 施加 guardbreak Tag |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| GuardStamina 属性注册 | P1 | 在 AttributeSet 定义 GuardStamina（当前值/最大值）；需随时间自动回复（Regen 系统已有，需配置） |
| GuardBreakSystem | P1 | 订阅 AttributeChanged(GuardStamina)，当值 ≤ 0 时：强制 RemoveEffect(BlockingBuff)、施加 Status.GuardBroken（固定 60 tick）；期间 blocking 输入被屏蔽 |
| 方向性减伤（可选） | P2 | 比较入射方向与施法者朝向，超出 ±60° 则不触发 Listener |

---

## 最佳实践

- **DO**: 减伤逻辑完全走 Phase Listener，不在 Ability Handler 里对伤害值硬编码。
- **DO**: `blocking` Tag 生命周期绑定 Effect 生命周期，Effect 移除则 Tag 自动清除。
- **DO**: GuardStamina 耗尽判定在独立 System 中做，不在 Graph 内做结构变更。
- **DON'T**: 不允许在 Graph 里直接 `RemoveEffect`；松开按键后由 Ability 的 block.End 阶段通过 Order 触发 Effect 移除。
- **DON'T**: 不允许 `MitigationCoeff` 硬编码在 Graph 程序中，必须经 `LoadConfigFloat` 读取模板参数。

---

## 验收口径

### 场景 1: 正常格挡减伤

| 项 | 内容 |
|----|------|
| **输入** | 玩家按住 Block 键，敌方攻击造成原始伤害 100；MitigationCoeff=0.15 |
| **预期输出** | 实际受到 FinalDamage=15；HP 减少 15；GuardStamina 减少 10 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.HoldBlock.BlockingBuff -> Status.Blocking granted` 及 `FinalDamage=15` |
| **截图要求** | 角色持盾动画持续；伤害浮字显示 15 |
| **多帧录屏** | F0: 按键 → F1: BlockingBuff Apply → F2: 攻击到达 → F3: Listener 执行 FinalDamage=15 → F4: HP 变化 → F5: 浮字 |

### 场景 2: 松开按键后恢复正常受伤

| 项 | 内容 |
|----|------|
| **输入** | 按住 Block 后松开，再受到 100 伤害 |
| **预期输出** | BlockingBuff 被移除，Status.Blocking Tag 消失，FinalDamage=100 |
| **Log 关键字** | `[GAS] RemoveEffect Effect.HoldBlock.BlockingBuff` 及 `[GAS] TagRemoved Status.Blocking` |
| **截图要求** | 盾收起动画；受伤浮字 100 |
| **多帧录屏** | F0: 松开 → F1: block.End → F2: Effect 移除 → F3: 受伤 100 |

### 场景 3: 格挡耐久耗尽破盾

| 项 | 内容 |
|----|------|
| **输入** | GuardStamina=30，受到连续 4 次格挡（每次 -10），第 4 次使 GuardStamina≤0 |
| **预期输出** | GuardBreakSystem 移除 BlockingBuff，施加 Status.GuardBroken；后续攻击全伤 |
| **Log 关键字** | `[GuardBreak] GuardStamina<=0, removing BlockingBuff, applying Status.GuardBroken` |
| **截图要求** | 角色受到硬直动画；盾破特效 |
| **多帧录屏** | F0-F3: 连续格挡 → F4: GuardStamina=0 → F5: 破盾系统触发 → F6: 无盾状态受全伤 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/HoldBlockTests.cs
[Test]
public void H1_BlockActive_DamageMitigated()
{
    // Arrange
    var world = CreateTestWorld();
    var blocker = SpawnUnit(world, hp: 500);
    var attacker = SpawnUnit(world, baseDamage: 100);
    ApplyEffect(world, blocker, "Effect.HoldBlock.BlockingBuff"); // 模拟按住

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, blocker, abilityId: "basic_attack");
    world.Tick(3);

    // Assert — MitigationCoeff=0.15 → FinalDamage=15
    Assert.AreEqual(485, GetAttribute(world, blocker, "Health")); // 500-15
}

[Test]
public void H1_BlockReleased_FullDamageApplied()
{
    var world = CreateTestWorld();
    var blocker = SpawnUnit(world, hp: 500);
    var attacker = SpawnUnit(world, baseDamage: 100);
    // 不施加 BlockingBuff → 全伤
    world.SubmitOrder(attacker, OrderType.CastAbility, blocker, abilityId: "basic_attack");
    world.Tick(3);
    Assert.AreEqual(400, GetAttribute(world, blocker, "Health"));
}

[Test]
public void H1_GuardStaminaDepleted_GuardBroken()
{
    var world = CreateTestWorld();
    var blocker = SpawnUnit(world, hp: 500, guardStamina: 10);
    ApplyEffect(world, blocker, "Effect.HoldBlock.BlockingBuff");
    // 单次格挡耗尽 GuardStamina
    var attacker = SpawnUnit(world, baseDamage: 100);
    world.SubmitOrder(attacker, OrderType.CastAbility, blocker, abilityId: "basic_attack");
    world.Tick(5);
    Assert.IsTrue(HasTag(world, blocker, "Status.GuardBroken"));
    Assert.IsFalse(HasEffect(world, blocker, "Effect.HoldBlock.BlockingBuff"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "H1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/defense/h1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/defense/h1_normal.png`
   - `artifacts/acceptance/defense/h1_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/defense/h1_60frames.gif`
   - 标注关键帧：按键帧、Buff Apply 帧、伤害减免帧、破盾帧

---

## 参考案例

- **《黑暗之魂》系列 盾格挡**: 持盾消耗体力槽，体力耗尽触发硬直，体力自动回复。
- **《守望先锋》莱因哈特 盾墙**: 盾有独立 HP 池，破盾后 CD 期间无法格挡。
- **《战神》奎托斯 L1格挡**: 松开即解除，无资源消耗但时序上存在弹反窗口（→H2）。
