# S5: 快速施放（Quick Cast）

> 按键时立即以鼠标位置为目标施放技能，无需二次确认。如：LoL Quick Cast 模式。

---

## 机制描述

玩家按下技能键时，技能立即以当前鼠标位置为目标施放，跳过预瞄准/指示器确认步骤。常见于：
- **英雄联盟（LoL）**：Quick Cast 设置
- **Dota 2**：Quick Cast 选项
- **风暴英雄（HotS）**：即时施放模式

与普通施放的区别：普通施放显示预瞄准界面（按键→显示指示器→再次点击确认），Quick Cast 跳过确认步骤（按键→立即施放）。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Position` / `Entity`
- **InteractionMode**: `SmartCast`（立即施放，无确认）

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/fireball.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "fireball",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Position",
  "isSkillMapping": true,
  "castModeOverride": "SmartCast",                // 强制 Quick Cast
  "autoTargetPolicy": "CursorPosition"            // 使用鼠标当前位置
}
```

**全局设置**：
```json5
// 配置路径: mods/<yourMod>/PlayerSettings.json
{
  "defaultInteractionMode": "SmartCast"           // 全局 Quick Cast
}
```

**关键点**：
- `InteractionModeType.SmartCast` 已实现，无需新增基建
- 鼠标位置在按键瞬间采样，直接填入 `Order.Args.Spatial`
- 可通过 `castModeOverride` 为单个技能覆盖全局设置

---

## Graph 实现

Quick Cast 的 Graph 实现与普通施放完全相同，差异仅在输入层的确认步骤。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/fireball_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnCalculate:
  LoadContextSource         E[0]          // 施法者
  LoadConfigFloat           "Radius" → F[0]
  QueryRadius               Order.Args.Spatial, F[0] → TargetList
  QueryFilterRelationship   Enemy
  QueryLimit                5

Phase OnApply:
  FanOutApplyEffect         TargetList, "Effect.Fireball.Damage"
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Fireball.Main",
  "presetType": "AoEDamage",
  "lifetime": { "kind": "Instant" },
  "configParams": {
    "Radius": 300.0,
    "DamageCoeff": 1.5
  },
  "grantedTags": ["Ability.Fireball.Active"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| InteractionModeType.SmartCast | `src/Core/Input/InteractionModeType.cs` | ✅ 已有 |
| InputOrderMappingSystem | `src/Core/Input/Orders/InputOrderMappingSystem.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 无 | - | 现有基建可完整表达。`InteractionModeType.SmartCast` 已实现，支持全局设置和 per-ability 覆盖。 |

---

## 最佳实践

- **DO**: 为 Quick Cast 提供可选的范围指示器（如半透明圆圈），帮助玩家判断技能范围。
- **DO**: 允许玩家在设置中切换 Quick Cast / 普通施放模式。
- **DO**: 为需要精确瞄准的技能（如狙击）保留普通施放模式，不强制 Quick Cast。
- **DON'T**: 不要在 Graph 层实现 Quick Cast 逻辑（属于 Input 层职责）。
- **DON'T**: 不要将 Quick Cast 与 S6（小地图施放）混淆：S5 是鼠标位置采样时机，S6 是坐标转换来源。

---

## 验收口径

### 场景 1: Quick Cast 正常施放

| 项 | 内容 |
|----|------|
| **输入** | 玩家按下火球术键（`PressedThisFrame`），鼠标位置：(1000, 500) |
| **预期输出** | 火球术立即在 (1000, 500) 施放，无预瞄准界面，范围内敌人受到伤害 |
| **Log 关键字** | `[Input] SmartCast detected, cursorPos=(1000, 500)` <br> `[Input] OrderSubmitted actionId=fireball spatial=(1000, 500)` <br> `[GAS] ApplyEffect Effect.Ability.Fireball.Main -> center=(1000, 500) targets=3` |
| **截图要求** | 火球术特效出现在鼠标位置，无预瞄准指示器，敌人受到伤害 |
| **多帧录屏** | F0: 按键 → F1: 立即施放（无确认步骤）→ F2: Effect Apply → F3: 伤害结算 |

### 场景 2: 普通施放（对比）

| 项 | 内容 |
|----|------|
| **输入** | 玩家按下火球术键（普通模式），显示预瞄准指示器，移动鼠标到 (1200, 600)，再次点击确认 |
| **预期输出** | 火球术在 (1200, 600) 施放，经过预瞄准→确认两步 |
| **Log 关键字** | `[Input] AimCast started, showing indicator` <br> `[Input] AimCast confirmed, spatial=(1200, 600)` <br> `[GAS] ApplyEffect Effect.Ability.Fireball.Main -> center=(1200, 600)` |
| **截图要求** | 预瞄准指示器出现，玩家移动鼠标调整位置，确认后施放 |
| **多帧录屏** | F0: 按键 → F1-F30: 显示指示器 → F31: 确认点击 → F32: 施放 |

### 场景 3: Quick Cast + 超出范围

| 项 | 内容 |
|----|------|
| **输入** | 按下火球术键，鼠标位置距离施法者 > 最大施法距离 |
| **预期输出** | 技能在最大施法距离边界施放（或激活失败，取决于设计） |
| **Log 关键字** | `[Input] SmartCast outOfRange, clamped to maxRange=800` <br> `[GAS] ApplyEffect Effect.Ability.Fireball.Main -> center=(clamped_pos)` |
| **截图要求** | 火球术在最大距离边界施放，或显示"超出范围"提示 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/QuickCastTests.cs
[Test]
public void S5_QuickCast_ImmediateCast()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0), mana: 100);
    var enemy1 = SpawnUnit(world, position: (1000, 500), hp: 500);
    var enemy2 = SpawnUnit(world, position: (1100, 500), hp: 500);
    var cursorPos = new Vector2(1000, 500);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, spatial: cursorPos, abilityId: "fireball");
    world.Tick(3);

    // Assert
    // 范围内敌人受到伤害
    Assert.Less(GetAttribute(world, enemy1, "Health"), 500);
    Assert.Less(GetAttribute(world, enemy2, "Health"), 500);
}

[Test]
public void S5_NormalCast_RequiresConfirmation()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0), mana: 100);
    var cursorPos = new Vector2(1000, 500);

    // Act
    world.PressKey(source, "Fireball");  // 第一次按键：显示指示器
    world.Tick(30);
    Assert.IsTrue(world.IsIndicatorActive("fireball"));  // 指示器显示中

    world.ClickConfirm(cursorPos);  // 第二次点击：确认施放
    world.Tick(3);

    // Assert
    Assert.IsFalse(world.IsIndicatorActive("fireball"));  // 指示器消失
    // 技能已施放
}

[Test]
public void S5_QuickCast_OutOfRange_Clamped()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0), mana: 100);
    var cursorPos = new Vector2(2000, 0);  // 超出最大距离 800

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, spatial: cursorPos, abilityId: "fireball");
    world.Tick(3);

    // Assert
    var actualPos = world.GetLastEffectPosition("Effect.Ability.Fireball.Main");
    Assert.AreEqual(800, actualPos.magnitude, 10);  // 被限制在最大距离
}
```

### 集成验收
1. 运行 `dotnet test --filter "S5"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s5_quick_cast_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s5_quick.png`（Quick Cast）
   - `artifacts/acceptance/special_input/s5_normal.png`（普通施放）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s5_60frames.gif`
   - 标注关键帧：按键帧、施放帧（Quick Cast 无确认帧）

---

## 参考案例

- **英雄联盟（LoL）Quick Cast**: 按键立即施放，无预瞄准界面，提升操作速度。
- **Dota 2 Quick Cast**: 可选的快速施放模式，适合熟练玩家。
- **风暴英雄（HotS）即时施放**: 默认 Quick Cast，简化操作流程。
