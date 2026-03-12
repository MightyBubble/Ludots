# P1: 选择额外目标

> 技能执行中途插入 SelectionGate，暂停执行并等待玩家选择额外目标（如：LoL Zed R 影分身选择、Dota Rubick 窃取技能目标）。

---

## 机制描述

选择额外目标允许技能在执行过程中暂停，等待玩家选择一个或多个额外目标，然后继续执行后续步骤。核心特征：
- **中途暂停**：技能已开始执行，在特定步骤暂停
- **目标选择**：玩家通过点击或框选选择实体
- **继续执行**：选择完成后，技能使用选定目标继续执行

与其他机制的区别：
- vs 03 单体指向：03 是施放前选择，P1 是执行中选择
- vs P6 多次选取：P6 是多次选择，P1 是单次额外选择

---

## 交互层设计

- **Trigger**: `PressedThisFrame`（初始触发）
- **SelectionType**: `Entity`（SelectionGate 阶段）
- **InteractionMode**: `SmartCast` 或 `TargetFirst`

```json5
// 配置路径: mods/<yourMod>/Abilities/<ability_id>.json
// AbilityExecSpec 定义，接口: src/Core/Gameplay/GAS/AbilityExecSpec.cs
{
  "abilityId": "Ability.Zed.R",
  "execSpec": {
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 0,
        "effectTemplateId": "Effect.Zed.R.MarkTarget"  // 标记主目标
      },
      {
        "kind": "SelectionGate",
        "tick": 30,                                     // 0.5s 后暂停等待选择
        "requestTagId": "Selection.Zed.ShadowSwap",
        "selectionType": "Entity",
        "maxCount": 1,
        "filterTagId": "Tag.Zed.Shadow",               // 仅可选择影分身
        "timeoutTicks": 180
      },
      {
        "kind": "EffectSignal",
        "tick": 31,
        "effectTemplateId": "Effect.Zed.R.SwapToShadow"
      }
    ]
  }
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// SelectionGate: src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs
// SelectionResponse: src/Core/Input/Selection/GasSelectionResponseSystem.cs

// Phase OnApply Main (Effect.Zed.R.MarkTarget)
Phase OnApply Main:
  LoadContextSource         E[0]          // Zed
  LoadContextTarget         E[1]          // 主目标
  LoadConfigInt             "MarkEffectId" → I[0]
  ApplyEffectDynamic        E[0], E[1], I[0]  // 给主目标打上 Mark Tag

// --- AbilityExecSystem 执行到 SelectionGate，挂起 ---
// GasSelectionResponseSystem 接收玩家点击
// 选定实体写入 AbilityExecInstance.SelectionResponse
// AbilityExecSystem 恢复执行，将 SelectionResponse 写入 Blackboard

// Phase OnApply Main (Effect.Zed.R.SwapToShadow)
Phase OnApply Main:
  LoadContextSource         E[0]          // Zed
  ReadBlackboardEntity      E[effect], "SelectedShadow" → E[2]  // 读取选定影分身

  // 获取影分身当前位置
  LoadAttribute             E[2], PositionX → F[0]
  LoadAttribute             E[2], PositionY → F[1]

  // 将传送目标坐标写入 Blackboard（由 TeleportRuntimeSystem 消费）
  WriteBlackboardFloat      E[effect], "TeleportX", F[0]
  WriteBlackboardFloat      E[effect], "TeleportY", F[1]
```

SelectionGate 配置说明：
```csharp
// src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs
// SelectionGate 会暂停 AbilityExecSystem，直到：
//   1. 玩家提交有效选择（filterTagId 匹配）
//   2. 超时（timeoutTicks 到期）
// 选中实体通过 Blackboard["SelectedShadow"] 传递给后续 EffectSignal
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| SelectionGate | `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs` | ✅ 已有 |
| GasSelectionResponseSystem | `src/Core/Input/Selection/GasSelectionResponseSystem.cs` | ✅ 已有 |
| AbilityExecSystem | `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` | ✅ 已有 |
| BlackboardEntityBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| GraphOps (ReadBlackboardEntity, ApplyEffectDynamic) | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 在 SelectionGate 前的 EffectSignal 中创建可选择的实体（如影分身），使玩家有实体可选。
- **DO**: 使用 `filterTagId` 限制可选范围，避免玩家选择无效目标。
- **DO**: 设置合理的 `timeoutTicks`，避免玩家长时间不操作导致技能卡死。
- **DON'T**: 不要在 SelectionGate 期间发布会改变可选目标集合的 Effect（状态一致性）。
- **DON'T**: 不要在 Graph 内直接读取原始输入状态，统一通过 Blackboard 传递选定实体。
- **DON'T**: 不要允许选择已死亡或持有 `Status.Untargetable` Tag 的实体（通过 filterTagId 排除）。

---

## 验收口径

### 场景 1: 正常选择影分身

| 项 | 内容 |
|----|------|
| **输入** | Zed 施放 R，tick=30 时 SelectionGate 打开，玩家点击影分身 A（位于 pos(500,0)） |
| **预期输出** | Zed 传送到位置 (500,0)；技能继续执行后续步骤 |
| **Log 关键字** | `[GAS] EffectSignal MarkTarget tick=0` → `[AbilityExec] SelectionGate opened requestTag=Selection.Zed.ShadowSwap` → `[Selection] SelectedEntity=<shadow_A>` → `[GAS] TeleportX=500 TeleportY=0` |
| **截图要求** | UI 显示影分身高亮，传送动画播放，Zed 出现在影分身位置 |
| **多帧录屏** | F0: 施放 R → F30: SelectionGate 打开 → F60: 玩家点击 → F61: 传送 |

### 场景 2: 超时未选择

| 项 | 内容 |
|----|------|
| **输入** | SelectionGate `timeoutTicks=180`，玩家在 180 ticks 内未选择任何目标 |
| **预期输出** | 超时后技能中断（或使用默认目标，根据配置） |
| **Log 关键字** | `[AbilityExec] SelectionGate timeout requestTag=Selection.Zed.ShadowSwap` → `[GAS] AbilityInterrupted reason=SelectionTimeout` |
| **截图要求** | UI 提示超时，技能取消动画 |
| **多帧录屏** | F0: Gate 打开 → F180: 超时 → F181: 中断 |

### 场景 3: 选择无效目标（filterTagId 不匹配）

| 项 | 内容 |
|----|------|
| **输入** | 玩家点击非影分身实体（无 `Tag.Zed.Shadow`） |
| **预期输出** | 选择被拒绝，Gate 保持打开，UI 提示重新选择 |
| **Log 关键字** | `[Selection] Rejected reason=FilterMismatch entity=<non_shadow>` |
| **截图要求** | 无效点击无高亮，选择框仍显示 |
| **多帧录屏** | F0: 点击无效目标 → F1: 拒绝 → F2: Gate 保持 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/InsertableContextTests.cs
[Test]
public void P1_SelectExtraTarget_NormalSelection_TeleportsToShadow()
{
    // Arrange
    var world = CreateTestWorld();
    var zed = SpawnUnit(world, posX: 0f, posY: 0f);
    var shadowA = SpawnUnit(world, posX: 500f, posY: 0f);
    AddTag(world, shadowA, "Tag.Zed.Shadow");

    // Act
    world.SubmitOrder(zed, OrderType.CastAbility, abilityId: "Ability.Zed.R");
    world.Tick(30);  // EffectSignal.MarkTarget → SelectionGate 打开

    // 模拟玩家选择 shadowA
    InjectSelectionResponse(world, zed, selectedEntity: shadowA);
    world.Tick(2);   // 传送执行

    // Assert
    Assert.AreEqual(500f, GetAttribute(world, zed, "PositionX"), "Zed 应传送到影分身位置");
}

[Test]
public void P1_SelectExtraTarget_Timeout_AbilityInterrupted()
{
    // SelectionGate timeoutTicks=180，玩家不操作
    // world.Tick(181)
    // Assert: ability state = Interrupted
}

[Test]
public void P1_SelectExtraTarget_InvalidFilter_SelectionRejected()
{
    // 玩家选择无 Tag.Zed.Shadow 的实体
    // Assert: gate still open, ability not progressed
}
```

### 集成验收
1. 运行 `dotnet test --filter "P1_SelectExtraTarget"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/insertable_context/p1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/insertable_context/p1_normal.png`
   - `artifacts/acceptance/insertable_context/p1_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/insertable_context/p1_60frames.gif`
   - 标注关键帧：施放帧、Gate 打开帧、选择帧、执行帧

---

## 参考案例

- **LoL Zed R**: 标记目标后，可选择传送到任意影分身位置（需点击选择）。
- **Dota Rubick 窃取**: 施放后选择目标，窃取其最后使用的技能。
- **Dota Chen 圣堂武士**: 命令野怪时中途选择释放对象（分体控制）。
