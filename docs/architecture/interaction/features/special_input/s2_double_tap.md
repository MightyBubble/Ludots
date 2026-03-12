# S2: 双击（Double Tap）

> 快速双击同一按键触发特殊技能。如：黑魂双击前进键翻滚，怪猎双击方向键冲刺。

---

## 机制描述

玩家在短时间窗口内（通常 15-30 ticks）连续按下同一按键两次，触发特殊技能或动作。常见于：
- **黑暗之魂**：双击前进键触发翻滚
- **怪物猎人**：双击移动键快速转向
- **Warframe**：双击方向键冲刺

与 S1（组合键）的区别：S2 是时序判断（两次按键的时间间隔），S1 是同时按下多个键。

---

## 交互层设计

- **Trigger**: `DoubleTap`（已废弃，需 P2 重新实现）
- **SelectionType**: 根据技能需求（`None` / `Direction`）
- **InteractionMode**: `Explicit`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/sprint.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "sprint",
  "trigger": "DoubleTap",                         // ❌ 已废弃，需 P2 实现
  "orderTypeKey": "castAbility",
  "selectionType": "None",
  "isSkillMapping": true,
  "doubleTapWindowTicks": 15                      // 双击窗口时间
}
```

**当前状态**：`InputTriggerType.DoubleTap` 已废弃，需要在 `SelectionSystem` 层实现双击检测。

**替代方案（Tag 模拟）**: 首次按键 → AddTag("first_press_{action}", duration=15 ticks)；第二次按键时检查 HasTag("first_press_{action}") → 若有则触发双击技能并移除 Tag。此方案无需修改 SelectionSystem，完全在现有 Tag + RequiredAll 框架内实现。

---

## Graph 实现

双击触发的技能 Graph 实现与普通技能相同，关键在于输入层的双击检测。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/sprint_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnApply:
  LoadContextSource         E[0]          // 施法者
  LoadConfigFloat           "SpeedBoost" → F[0]  // 冲刺速度加成
  ModifyAttributeAdd        E[effect], E[0], MoveSpeed, F[0]
  // 授予 Tag 持续 N ticks
  GrantedTags: ["Status.Sprinting"]
  Lifetime: { "kind": "Duration", "durationTicks": 60 }
```

Effect 模板示例：
```json5
{
  "id": "Effect.Sprint.Main",
  "presetType": "Buff",
  "lifetime": { "kind": "Duration", "durationTicks": 60 },
  "configParams": {
    "SpeedBoost": 200.0                   // 移动速度 +200
  },
  "grantedTags": ["Status.Sprinting"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| SelectionSystem | `src/Core/Input/Selection/SelectionSystem.cs` | ❌ P2 — 需新增双击检测逻辑 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| SelectionSystem 双击检测 | P2 | `InputTriggerType.DoubleTap` 已废弃，需在 SelectionSystem 中实现：记录上次按键时间，判断两次按键间隔是否 < doubleTapWindowTicks。 |

**实现思路**：
```csharp
// src/Core/Input/Selection/SelectionSystem.cs
private Dictionary<string, int> _lastPressedTick = new();

public void Update(int currentTick)
{
    foreach (var mapping in _mappings)
    {
        if (mapping.Trigger == InputTriggerType.DoubleTap)
        {
            if (IsPressed(mapping.ActionId))
            {
                if (_lastPressedTick.TryGetValue(mapping.ActionId, out var lastTick))
                {
                    if (currentTick - lastTick <= mapping.DoubleTapWindowTicks)
                    {
                        // 触发双击
                        SubmitOrder(mapping);
                        _lastPressedTick.Remove(mapping.ActionId);
                        continue;
                    }
                }
                _lastPressedTick[mapping.ActionId] = currentTick;
            }
        }
    }
}
```

---

## 最佳实践

- **DO**: 双击窗口时间设为 15-20 ticks（250-333ms），太短难触发，太长误触发。
- **DO**: 双击触发后清除状态，避免三击变成双击+单击。
- **DO**: 为双击技能提供视觉反馈（如第一次按键时显示淡淡的提示圈）。
- **DON'T**: 不要在 Graph 层实现双击检测（属于 Input 层职责）。
- **DON'T**: 不要将双击与长按混淆：双击是两次 `PressedThisFrame`，长按是 `Held`。

---

## 验收口径

### 场景 1: 正常双击触发

| 项 | 内容 |
|----|------|
| **输入** | 玩家在 15 ticks 内按下前进键两次 |
| **预期输出** | 触发冲刺技能，移动速度 +200，持续 60 ticks |
| **Log 关键字** | `[Input] DoubleTapDetected actionId=sprint interval=12 ticks` <br> `[GAS] ApplyEffect Effect.Sprint.Main -> source=<sid> MoveSpeed=+200` |
| **截图要求** | 角色周围出现冲刺特效（如速度线），移动速度明显加快 |
| **多帧录屏** | F0: 第一次按键 → F12: 第二次按键 → F13: 冲刺触发 → F73: 冲刺结束 |

### 场景 2: 间隔过长（未触发）

| 项 | 内容 |
|----|------|
| **输入** | 玩家按下前进键，间隔 20 ticks 后再按一次（超过窗口） |
| **预期输出** | 不触发冲刺，两次按键均视为普通移动 |
| **Log 关键字** | `[Input] DoubleTapTimeout actionId=sprint interval=20 ticks` |
| **截图要求** | 无冲刺特效，正常移动 |
| **多帧录屏** | F0: 第一次按键 → F20: 第二次按键 → 无冲刺触发 |

### 场景 3: 冲刺中再次双击

| 项 | 内容 |
|----|------|
| **输入** | 冲刺持续期间再次双击前进键 |
| **预期输出** | 刷新冲刺持续时间（或无效，取决于设计） |
| **Log 关键字** | `[GAS] EffectRefreshed Effect.Sprint.Main duration=60 ticks` |
| **截图要求** | 冲刺特效持续，计时器重置 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/DoubleTapTests.cs
[Test]
public void S2_DoubleTap_WithinWindow_TriggersSprint()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, moveSpeed: 300);

    // Act
    world.PressKey(source, "Forward");
    world.Tick(12);
    world.PressKey(source, "Forward");  // 间隔 12 ticks < 15
    world.Tick(1);

    // Assert
    Assert.AreEqual(500, GetAttribute(world, source, "MoveSpeed"));  // 300 + 200
    Assert.IsTrue(HasTag(world, source, "Status.Sprinting"));
}

[Test]
public void S2_DoubleTap_ExceedWindow_NoTrigger()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, moveSpeed: 300);

    // Act
    world.PressKey(source, "Forward");
    world.Tick(20);
    world.PressKey(source, "Forward");  // 间隔 20 ticks > 15
    world.Tick(1);

    // Assert
    Assert.AreEqual(300, GetAttribute(world, source, "MoveSpeed"));  // 未加速
    Assert.IsFalse(HasTag(world, source, "Status.Sprinting"));
}

[Test]
public void S2_DoubleTap_DuringSprint_RefreshDuration()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, moveSpeed: 300);
    world.PressKey(source, "Forward");
    world.Tick(12);
    world.PressKey(source, "Forward");
    world.Tick(50);  // 冲刺剩余 10 ticks

    // Act
    world.PressKey(source, "Forward");
    world.Tick(12);
    world.PressKey(source, "Forward");  // 再次双击
    world.Tick(1);

    // Assert
    var effect = GetEffect(world, source, "Effect.Sprint.Main");
    Assert.AreEqual(60, effect.RemainingTicks);  // 刷新为 60
}
```

### 集成验收
1. 运行 `dotnet test --filter "S2"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s2_double_tap_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s2_normal.png`（单次按键）
   - `artifacts/acceptance/special_input/s2_sprint.png`（双击冲刺）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s2_60frames.gif`
   - 标注关键帧：第一次按键、第二次按键、冲刺触发、冲刺结束

---

## 参考案例

- **黑暗之魂（Dark Souls）翻滚**: 双击前进键触发翻滚，消耗耐力，有无敌帧。
- **怪物猎人（Monster Hunter）快速转向**: 双击移动键快速转身，用于调整攻击方向。
- **Warframe 冲刺**: 双击方向键触发冲刺，持续到松开移动键或耐力耗尽。
