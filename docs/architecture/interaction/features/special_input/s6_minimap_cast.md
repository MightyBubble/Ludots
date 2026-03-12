# S6: 小地图施放（Minimap Cast）

> 在小地图上点击施放技能，技能以小地图点击位置对应的世界坐标为目标。如：Dota 2 小地图传送。

---

## 机制描述

玩家在小地图上点击施放技能，技能以小地图点击位置对应的世界坐标为目标，无需切换视角。常见于：
- **Dota 2**：小地图右键移动/施放全局技能
- **英雄联盟（LoL）**：小地图施放传送
- **星际争霸（StarCraft）**：小地图点击移动

与 S5（Quick Cast）的区别：S5 是鼠标位置采样时机，S6 是坐标转换来源（小地图→世界）。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: `Position`（小地图坐标→世界坐标）
- **InteractionMode**: `Explicit`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/teleport.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "teleport",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "Position",
  "isSkillMapping": true,
  "inputSource": "Minimap"                        // ❌ P3 — 需新增 Minimap 输入源
}
```

**关键点**：
- 需要 `MinimapInputAdapter` 将小地图点击坐标转换为世界坐标
- 转换公式：`worldPos = worldMin + (minimapUV * worldSize)`
- 小地图点击与世界点击共享相同的 `Position` 选择逻辑，只是坐标转换来源不同

---

## Graph 实现

小地图施放的 Graph 实现与普通位置施放完全相同，差异仅在坐标转换。

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/teleport_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

Phase OnApply:
  LoadContextSource         E[0]          // 施法者
  LoadConfigFloat           "TeleportDelay" → F[0]
  // 延迟 N ticks 后传送
  GrantedTags: ["Status.Channeling"]
  Lifetime: { "kind": "Duration", "durationTicks": F[0] }

Phase OnExpire:
  LoadContextSource         E[0]
  // 将施法者位置设为 Order.Args.Spatial
  // ❌ Graph 无法直接修改 Position 组件，需通过 AttributeSink 或 CommandBuffer
```

Effect 模板示例：
```json5
{
  "id": "Effect.Ability.Teleport.Main",
  "presetType": "Teleport",
  "lifetime": { "kind": "Duration", "durationTicks": 180 },  // 3 秒引导
  "configParams": {
    "TeleportDelay": 180
  },
  "grantedTags": ["Status.Channeling"],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有 |
| MinimapInputAdapter | `src/Core/Input/Minimap/MinimapInputAdapter.cs` | ❌ P3 — 需新增小地图点击→世界坐标转换 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| MinimapInputAdapter | P3 | 需实现小地图点击坐标→世界坐标转换。转换公式：`worldPos = worldMin + (minimapUV * worldSize)`，其中 `minimapUV` 是小地图点击位置的归一化坐标（0-1）。 |

**实现思路**：
```csharp
// src/Core/Input/Minimap/MinimapInputAdapter.cs
public class MinimapInputAdapter
{
    private Rect _minimapBounds;  // 小地图屏幕区域
    private Rect _worldBounds;    // 世界坐标范围

    public Vector2 MinimapToWorld(Vector2 screenPos)
    {
        // 1. 判断点击是否在小地图区域内
        if (!_minimapBounds.Contains(screenPos))
            return Vector2.Zero;

        // 2. 计算归一化坐标（0-1）
        var minimapUV = new Vector2(
            (screenPos.x - _minimapBounds.xMin) / _minimapBounds.width,
            (screenPos.y - _minimapBounds.yMin) / _minimapBounds.height
        );

        // 3. 转换为世界坐标
        var worldPos = new Vector2(
            _worldBounds.xMin + minimapUV.x * _worldBounds.width,
            _worldBounds.yMin + minimapUV.y * _worldBounds.height
        );

        return worldPos;
    }
}
```

---

## 最佳实践

- **DO**: 为小地图施放提供视觉反馈（如小地图上显示技能范围圈）。
- **DO**: 在小地图点击时播放音效，提示玩家操作已生效。
- **DO**: 允许玩家取消小地图施放（如按 ESC 键）。
- **DON'T**: 不要在 Graph 层实现坐标转换（属于 Input 层职责）。
- **DON'T**: 不要将小地图施放与 S5（Quick Cast）混淆：S6 是坐标转换来源，S5 是采样时机。

---

## 验收口径

### 场景 1: 小地图施放传送

| 项 | 内容 |
|----|------|
| **输入** | 玩家按下传送键，点击小地图位置（minimapUV = 0.8, 0.6），世界范围 (0, 0) - (10000, 10000) |
| **预期输出** | 传送技能以世界坐标 (8000, 6000) 为目标，引导 3 秒后传送 |
| **Log 关键字** | `[Input] MinimapClick detected, screenPos=(1200, 800) minimapUV=(0.8, 0.6)` <br> `[Input] MinimapToWorld worldPos=(8000, 6000)` <br> `[Input] OrderSubmitted actionId=teleport spatial=(8000, 6000)` <br> `[GAS] ApplyEffect Effect.Ability.Teleport.Main -> target=(8000, 6000)` |
| **截图要求** | 小地图上显示传送目标标记，引导条出现，3 秒后角色传送到目标位置 |
| **多帧录屏** | F0: 点击小地图 → F1: 坐标转换 → F2: 引导开始 → F182: 传送完成 |

### 场景 2: 小地图移动（非技能）

| 项 | 内容 |
|----|------|
| **输入** | 玩家右键点击小地图位置（minimapUV = 0.5, 0.5） |
| **预期输出** | 单位移动到世界坐标 (5000, 5000) |
| **Log 关键字** | `[Input] MinimapClick detected, worldPos=(5000, 5000)` <br> `[Input] OrderSubmitted orderType=Move spatial=(5000, 5000)` |
| **截图要求** | 单位开始移动到目标位置，小地图上显示移动路径 |
| **多帧录屏** | F0: 点击小地图 → F1-F300: 移动过程 → F301: 到达目标 |

### 场景 3: 小地图施放超出范围

| 项 | 内容 |
|----|------|
| **输入** | 按下传送键，点击小地图位置，但距离 > 最大施法距离 |
| **预期输出** | 激活失败；UI 提示"超出范围" |
| **Log 关键字** | `[GAS] AbilityActivationFailed actionId=teleport reason=OutOfRange distance=12000 maxRange=10000` |
| **截图要求** | 技能图标闪烁红色，小地图上显示"超出范围"提示 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/MinimapCastTests.cs
[Test]
public void S6_MinimapClick_ConvertsToWorldPos()
{
    // Arrange
    var adapter = new MinimapInputAdapter(
        minimapBounds: new Rect(1000, 700, 200, 200),
        worldBounds: new Rect(0, 0, 10000, 10000)
    );
    var screenPos = new Vector2(1160, 820);  // minimapUV = (0.8, 0.6)

    // Act
    var worldPos = adapter.MinimapToWorld(screenPos);

    // Assert
    Assert.AreEqual(8000, worldPos.x, 10);
    Assert.AreEqual(6000, worldPos.y, 10);
}

[Test]
public void S6_MinimapTeleport_TriggersAtWorldPos()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0), mana: 100);
    var targetPos = new Vector2(8000, 6000);

    // Act
    world.SubmitOrder(source, OrderType.CastAbility, spatial: targetPos, abilityId: "teleport");
    world.Tick(180);  // 引导 3 秒

    // Assert
    var currentPos = GetPosition(world, source);
    Assert.AreEqual(8000, currentPos.x, 10);
    Assert.AreEqual(6000, currentPos.y, 10);
}

[Test]
public void S6_MinimapClick_OutsideBounds_Ignored()
{
    // Arrange
    var adapter = new MinimapInputAdapter(
        minimapBounds: new Rect(1000, 700, 200, 200),
        worldBounds: new Rect(0, 0, 10000, 10000)
    );
    var screenPos = new Vector2(500, 500);  // 不在小地图区域内

    // Act
    var worldPos = adapter.MinimapToWorld(screenPos);

    // Assert
    Assert.AreEqual(Vector2.Zero, worldPos);  // 返回零向量
}
```

### 集成验收
1. 运行 `dotnet test --filter "S6"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s6_minimap_cast_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s6_teleport.png`（小地图传送）
   - `artifacts/acceptance/special_input/s6_move.png`（小地图移动）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s6_60frames.gif`
   - 标注关键帧：小地图点击、坐标转换、引导开始、传送完成

---

## 参考案例

- **Dota 2 小地图操作**: 右键小地图移动，技能+小地图点击施放全局技能（如传送、全图技能）。
- **英雄联盟（LoL）传送**: 点击小地图上的防御塔/小兵施放传送，无需切换视角。
- **星际争霸（StarCraft）小地图移动**: 右键小地图快速移动单位，提升操作效率。
