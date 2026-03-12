# F1: 蓄力+方向射击

> 按住蓄力，松开时以当前瞄准方向射出弹道；蓄力时长决定伤害与弹速缩放。典型案例：LoL Varus Q、OW 黑百合/源氏。

---

## 机制描述

玩家按住技能键开始蓄力，`charge_amount` 属性通过每帧 Effect 累积（0→1）。松开后读取当前 Cursor 方向 + `charge_amount`，发射伤害与速度随蓄力比例缩放的弹道。蓄力中播放充能动画，超过最大时长自动保持满蓄（不自动释放，F8 才自动释放）。

与 F8 的区别：F1 必须手动松开才触发，满蓄不自动释放。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`（Down 边沿触发 .Start）+ `ReleasedThisFrame`（Up 边沿触发 .End）
- **SelectionType**: `Direction`
- **InteractionMode**: `SmartCastWithIndicator`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/ability_Q.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_Q",
  "trigger": "PressedThisFrame",          // Down 边沿生成 .Start order
  "orderTypeKey": "castAbility",
  "selectionType": "Direction",
  "isSkillMapping": true,
  "castModeOverride": "SmartCastWithIndicator",
  "heldPolicy": "StartEnd"               // 自动生成 .Start / .End 对
  // ReleasedThisFrame → suffix ".End" order，无需第二个 mapping
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// ── Effect: charge_accumulator_effect（每 tick 触发 OnPeriod）──
Phase OnPeriod:
  LoadSelfAttribute         ChargeAmount       → F[0]
  LoadConfigFloat           "DeltaPerTick"     → F[1]   // 配置: 1.0/MaxChargeTicks
  AddFloat                  F[0], F[1]         → F[2]
  ConstFloat                0.0                → F[3]
  ConstFloat                1.0                → F[4]
  ClampFloat                F[2], F[3], F[4]   → F[5]
  WriteSelfAttribute        ChargeAmount,      F[5]
  // 同时写入 Blackboard 供 Indicator 读取
  LoadContextSource                            → E[0]
  WriteBlackboardFloat      E[0], ChargeRatio, F[5]

// ── Effect: release_projectile_effect（.End order 触发）──
Phase OnCalculate:
  LoadContextSource                            → E[0]
  LoadSelfAttribute         ChargeAmount       → F[0]   // 当前蓄力比例
  LoadAttribute             E[0], BaseDamage   → F[1]
  MulFloat                  F[1], F[0]         → F[2]   // 按比例缩伤
  LoadConfigFloat           "DamageCoeff"      → F[3]
  MulFloat                  F[2], F[3]         → F[4]
  WriteBlackboardFloat      E[0], DamageAmount, F[4]
  // 弹速缩放写入 Blackboard 供 ProjectileRuntimeSystem 读取
  LoadConfigFloat           "BaseSpeedCm"      → F[5]
  MulFloat                  F[5], F[0]         → F[6]
  WriteBlackboardFloat      E[0], ProjectileSpeedCm, F[6]

Phase OnApply Main:
  ReadBlackboardFloat       E[0], DamageAmount → F[0]
  LoadContextTarget                            → E[1]
  ModifyAttributeAdd        E[0], E[1], Health, NegFloat(F[0])
  // 释放后重置 ChargeAmount
  ConstFloat                0.0                → F[1]
  WriteSelfAttribute        ChargeAmount,      F[1]
```

Effect 模板示例：
```json5
// mods/<yourMod>/Effects/Effect.F1.ChargeAccumulator.json
{
  "id": "Effect.F1.ChargeAccumulator",
  "presetType": "Custom",
  "lifetime": { "kind": "Duration", "ticks": 120 },   // 最大蓄力帧数
  "period": 1,                                          // 每帧触发 OnPeriod
  "configParams": {
    "DeltaPerTick": 0.00833                             // 1.0 / 120
  },
  "grantedTags": ["Status.Charging"],
  "phaseListeners": []
}

// mods/<yourMod>/Effects/Effect.F1.ReleaseProjectile.json
{
  "id": "Effect.F1.ReleaseProjectile",
  "presetType": "InstantDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "DamageCoeff": 1.8,
    "BaseSpeedCm": 1200
  },
  "grantedTags": [],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| HeldPolicy.StartEnd | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| AbilityExecSpec EventGate | `src/Core/Gameplay/GAS/AbilityExecSpec.cs` | ✅ 已有 |
| LoadSelfAttribute / WriteSelfAttribute | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| EffectClip duration + period | `src/Core/Gameplay/GAS/Components/EffectClip.cs` | ✅ 已有 |
| ProjectileRuntimeSystem | `src/Core/Gameplay/GAS/Systems/ProjectileRuntimeSystem.cs` | ✅ 已有 |
| WriteBlackboardFloat | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |
| CursorDirectionBlackboardWriter | `src/Core/Input/Systems/CursorDirectionBlackboardWriter.cs` | ❌ P1 — 需新增 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| CursorDirectionBlackboardWriter | P1 | 蓄力期间每帧将鼠标/摇杆方向写入施法者 Blackboard（`CursorDirectionRad`），供 Indicator 渲染和 release 时读取发射角度；缺此组件，方向弹道无法跟随实时瞄准 |

---

## 最佳实践

- **DO**: 蓄力积累走 `OnPeriod` Graph + `WriteSelfAttribute`，不在 System 里直接写属性。
- **DO**: 弹速缩放写入 Blackboard（`ProjectileSpeedCm`），ProjectileRuntimeSystem 从 Blackboard 读取，不在 Effect 硬编码。
- **DO**: 松开后在 `OnApply` 末尾重置 `ChargeAmount = 0`，避免下次蓄力起点异常。
- **DON'T**: 不允许在 Graph 内创建弹道实体，必须通过 `RuntimeEntitySpawnQueue`。
- **DON'T**: 不用 `DoubleTap` InputTriggerType（已废弃）。
- **DON'T**: 蓄力上限（ClampFloat 0→1）必须在 Graph 内完成，不在 System 层做二次 clamp。

---

## 验收口径

### 场景 1: 正常蓄力+释放

| 项 | 内容 |
|----|------|
| **输入** | 按住 Q 键 60 帧（50% 蓄力），指向正东方向，松开 |
| **预期输出** | 弹道以 `BaseSpeedCm×0.5=600cm/tick` 向东飞行；伤害 = `BaseDamage×0.5×DamageCoeff`；`ChargeAmount` 重置为 0 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.F1.ReleaseProjectile -> DamageAmount=<v> ProjectileSpeedCm=600` |
| **截图要求** | 充能进度条达到 50%；弹道轨迹与鼠标方向一致 |
| **多帧录屏** | F0: 按键 → F1: `.Start` Order → F2: `charge_accumulator` 启动 → F60: 松键 → F61: `.End` Order → F62: 弹道生成 → F63: 命中目标 HP 变化 |

### 场景 2: 满蓄（120帧）后释放

| 项 | 内容 |
|----|------|
| **输入** | 按住 Q 键 150 帧（超过最大），松开 |
| **预期输出** | `ChargeAmount = 1.0`（ClampFloat 截断）；满伤害发射；不自动释放 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.F1.ReleaseProjectile -> DamageAmount=<maxDmg>` |
| **截图要求** | 充能进度条满格；弹道粒子效果最亮 |
| **多帧录屏** | F120 充能满格动画；F150 松键帧弹道出现 |

### 场景 3: 资源不足时按下

| 项 | 内容 |
|----|------|
| **输入** | Mana < Cost（Cost=50，Mana=20） |
| **预期输出** | `.Start` Order 被拒绝；不启动 `charge_accumulator`；无充能动画 |
| **Log 关键字** | `[GAS] AbilityActivationFailed reason=InsufficientMana abilityId=F1_ChargeShot` |
| **截图要求** | 无进度条；技能图标闪红 |
| **多帧录屏** | 仅按键帧，后续帧静止 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/ChargeHoldTests.cs
[Test]
public void F1_HalfCharge_DamageScaledCorrectly()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 100);
    var target = SpawnUnit(world, hp: 500, armor: 0);
    SetAttribute(world, source, "ChargeAmount", 0f);

    // Act: 模拟按住 60 帧后释放（charge=0.5）
    world.SubmitOrder(source, OrderType.CastAbility, target,
        abilityId: "F1_ChargeShot", suffix: ".Start");
    world.Tick(60);
    world.SubmitOrder(source, OrderType.CastAbility, target,
        abilityId: "F1_ChargeShot", suffix: ".End");
    world.Tick(3);

    // Assert: DamageAmount = 200 * 0.5 * 1.8 = 180
    Assert.AreEqual(320, GetAttribute(world, target, "Health"));
    Assert.AreEqual(0f, GetAttribute(world, source, "ChargeAmount"));
}

[Test]
public void F1_OverMaxCharge_ClampedTo1()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 100);
    var target = SpawnUnit(world, hp: 1000, armor: 0);

    world.SubmitOrder(source, OrderType.CastAbility, target,
        abilityId: "F1_ChargeShot", suffix: ".Start");
    world.Tick(200); // 超过 120 帧上限
    world.SubmitOrder(source, OrderType.CastAbility, target,
        abilityId: "F1_ChargeShot", suffix: ".End");
    world.Tick(3);

    // 满蓄: 200 * 1.0 * 1.8 = 360
    Assert.AreEqual(640, GetAttribute(world, target, "Health"));
}

[Test]
public void F1_InsufficientMana_ActivationFailed()
{
    var world = CreateTestWorld();
    var source = SpawnUnit(world, baseDamage: 200, mana: 20); // Cost=50
    var target = SpawnUnit(world, hp: 500, armor: 0);

    world.SubmitOrder(source, OrderType.CastAbility, target,
        abilityId: "F1_ChargeShot", suffix: ".Start");
    world.Tick(60);

    // ChargeAmount 未增加，HP 未变
    Assert.AreEqual(0f, GetAttribute(world, source, "ChargeAmount"));
    Assert.AreEqual(500, GetAttribute(world, target, "Health"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "F1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/charge_hold/f1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/charge_hold/f1_normal.png`（50% 蓄力发射）
   - `artifacts/acceptance/charge_hold/f1_edge.png`（满蓄发射）
4. 多帧录屏：
   - `artifacts/acceptance/charge_hold/f1_60frames.gif`
   - 关键帧标注：按键帧（F0）、蓄力进度条帧（F30/F60）、释放帧、弹道命中帧

---

## 参考案例

- **LoL Varus Q（蓄力之箭）**: 按住 Q 蓄力 2 秒，松开射出，蓄力越久箭矢越大伤害越高。
- **OW 黑百合（右键）**: 按住蓄力提升精准度与弹速，松开发射；蓄力时移动速度不受限。
