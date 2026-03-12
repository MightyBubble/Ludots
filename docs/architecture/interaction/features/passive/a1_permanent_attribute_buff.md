# A1: 永久属性加成

> 装备或被动天赋上线时施加 Duration=Permanent 的 GameplayEffect，AttributeAggregatorSystem 持续聚合修改量。典型案例：LoL 装备攻击力/护甲加成，Dota 各英雄天赋属性点。

---

## 机制描述

实体生成（spawn）或装备装备时，立即施加一个生命周期为 Permanent 的 GameplayEffect。该 Effect 携带 EffectModifiers（Add / Multiply），由 AttributeAggregatorSystem 每帧将 Base + Modifiers 聚合为 Current 值。Effect 存续期间属性持续有效；Effect 被移除（卸下装备）后属性恢复基础值。与 Duration 类 Effect 的核心区别：Permanent Effect 不随时间到期，只能被显式 Remove。

---

## 交互层设计

- **Trigger**: N/A（被动，无输入触发）
- **SelectionType**: N/A
- **InteractionMode**: N/A

```json5
// 无 InputOrderMapping — 被动在 entity spawn 时或装备时由系统直接 ApplyEffectTemplate
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
// 此机制不经过输入层
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:   mods/<yourMod>/Effects/passive_stat_buff.json
// 注册中心:      src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// 永久属性加成无需 Graph 程序，完全由 EffectModifiers 数据驱动
// AttributeAggregatorSystem 自动聚合:
//   Current = Base + Sum(Add modifiers) * Product(Multiply modifiers)
//
// 若需要按条件动态调整加成（如装备特殊效果），可在 OnApply Phase 中读写：

Phase OnApply Main:
  LoadContextTarget         E[1]              // 装备所有者
  LoadAttribute             E[1], BaseAttack → F[0]
  LoadConfigFloat           "BonusFlat"      → F[1]
  AddFloat                  F[0], F[1]       → F[2]
  // 注意：直接属性加成走 EffectModifiers，不在此处 WriteSelfAttribute
  // Graph 仅用于派生属性或条件加成逻辑
```

Effect 模板：
```json5
{
  "id": "Effect.Passive.A1.StatBuff",
  "presetType": "None",                      // EffectPresetType.None
  "lifetime": { "kind": "Permanent" },       // LifetimeKind.Permanent
  "modifiers": [
    { "attribute": "AttackDamage", "op": "Add",      "value": 30 },
    { "attribute": "MoveSpeed",    "op": "Multiply", "value": 1.1 }
  ],
  "grantedTags": [],
  "phaseListeners": []
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| AttributeBuffer | `src/Core/Gameplay/GAS/Components/AttributeBuffer.cs` | ✅ 已有 |
| EffectModifiers | `src/Core/Gameplay/GAS/Components/EffectModifiers.cs` | ✅ 已有 |
| AttributeAggregatorSystem | `src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs` | ✅ 已有 |
| EffectLifetimeSystem | `src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达永久属性加成。

---

## 最佳实践

- **DO**: 静态数值加成（固定值 Add/Multiply）完全走 EffectModifiers 数据配置，不写 Graph 程序。
- **DO**: 装备卸下时显式调用 RemoveEffect（按 Effect 实例 ID），确保 Modifier 被正确撤销。
- **DO**: 多件同类装备使用独立 Effect 实例，SameTypePolicy = Stack，避免 Replace 覆盖导致只有最后一件生效。
- **DON'T**: 不允许在 entity spawn 时直接修改 AttributeBuffer.Base 来"模拟"装备加成——Base 是角色基础属性，装备加成必须走 Modifier，否则卸下装备无法正确回滚。
- **DON'T**: 不允许将 Permanent Effect 与 EffectModifiers 以外的 runtime 状态（如 Blackboard 值）混用来存储装备状态——装备状态应挂在独立组件上，Effect 只负责属性修改。

---

## 验收口径

### 场景 1: 正常施加路径

| 项 | 内容 |
|----|------|
| **输入** | 实体 spawn 时携带配置 `passive_stat_buff`；或玩家装备道具触发 ApplyEffectTemplate |
| **预期输出** | `AttackDamage.Current = Base + 30`；`MoveSpeed.Current = Base × 1.1`；数值立即生效 |
| **Log 关键字** | `[GAS] ApplyEffect Effect.Passive.A1.StatBuff -> target=<tid> lifetime=Permanent` |
| **截图要求** | 属性面板显示加成后数值，装备槽图标高亮 |
| **多帧录屏** | F0: 装备事件 → F1: ApplyEffect 入队 → F2: AttributeAggregator 聚合 → F3: UI 属性面板更新 |

### 场景 2: 卸下装备，属性回滚

| 项 | 内容 |
|----|------|
| **输入** | 玩家卸下装备，触发 RemoveEffect（使用 spawn 时返回的 Effect 实例 ID） |
| **预期输出** | `AttackDamage.Current` 回到 Base 值；MoveSpeed 同理；UI 立即刷新 |
| **Log 关键字** | `[GAS] RemoveEffect Effect.Passive.A1.StatBuff -> target=<tid>` |
| **截图要求** | 属性面板数值恢复基础值，装备槽空置 |
| **多帧录屏** | F0: 卸装事件 → F1: RemoveEffect → F2: Aggregator 重聚合 → F3: UI 更新 |

### 场景 3: 多件同类装备叠加

| 项 | 内容 |
|----|------|
| **输入** | 同一单位装备 2 件 `AttackDamage+30` 的道具（各生成独立 Effect 实例） |
| **预期输出** | `AttackDamage.Current = Base + 60`；卸下其中一件后 `= Base + 30` |
| **Log 关键字** | `[GAS] ApplyEffect ... SameTypePolicy=Stack count=2` |
| **截图要求** | 属性面板显示 +60，卸一件后 +30 |
| **多帧录屏** | F0: 装备1 → F1: 装备2 → F2: 属性=Base+60 → F10: 卸装备1 → F11: 属性=Base+30 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/PermanentAttributeBuffTests.cs

[Test]
public void A1_StatBuff_Applied_AttributeIncreasesCorrectly()
{
    // Arrange
    var world = CreateTestWorld();
    var unit = SpawnUnit(world, attackDamage: 100, moveSpeed: 300);

    // Act
    ApplyPermanentEffect(world, unit, attackDamageAdd: 30, moveSpeedMul: 1.1f);
    world.Tick(1); // AttributeAggregator 聚合

    // Assert
    Assert.AreEqual(130, GetAttribute(world, unit, "AttackDamage"));
    Assert.AreEqual(330, GetAttribute(world, unit, "MoveSpeed")); // 300 * 1.1
}

[Test]
public void A1_StatBuff_Removed_AttributeReverts()
{
    var world = CreateTestWorld();
    var unit = SpawnUnit(world, attackDamage: 100);
    var effectId = ApplyPermanentEffect(world, unit, attackDamageAdd: 30);
    world.Tick(1);

    RemoveEffect(world, unit, effectId);
    world.Tick(1);

    Assert.AreEqual(100, GetAttribute(world, unit, "AttackDamage"));
}

[Test]
public void A1_TwoItems_Stack_DoubleBuff()
{
    var world = CreateTestWorld();
    var unit = SpawnUnit(world, attackDamage: 100);
    ApplyPermanentEffect(world, unit, attackDamageAdd: 30);
    ApplyPermanentEffect(world, unit, attackDamageAdd: 30);
    world.Tick(1);

    Assert.AreEqual(160, GetAttribute(world, unit, "AttackDamage"));
}
```

### 集成验收
1. 运行 `dotnet test --filter "A1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/passive/a1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/passive/a1_normal.png`（装备后属性面板）
   - `artifacts/acceptance/passive/a1_edge.png`（卸装后回滚）
4. 多帧录屏：
   - `artifacts/acceptance/passive/a1_60frames.gif`
   - 标注关键帧：装备帧、聚合帧、UI 更新帧、卸装帧

---

## 参考案例

- **LoL 长剑**: 装备获得 +10 攻击力，纯 Flat Add Modifier，无条件无 Graph，卸下立即回滚。
- **Dota2 力量英雄天赋**: 每点力量 +19 HP，属性聚合时由 DerivedGraph 将 STR×19 写入 MaxHP Modifier。
- **WoW 装备词缀**: 同类词缀多件叠加，SameTypePolicy=Stack，上限由 MaxStack 配置控制。
