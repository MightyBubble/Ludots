# O1: 陷阱/反制激活窗口

> 当对方效果进入 Proposal 阶段时，弹出响应窗口询问玩家是否激活反制手段；玩家在时限内决策，超时自动通过。典型案例：游戏王陷阱牌激活、LoL Sivir E（法术盾手动激活）。

---

## 机制描述

敌方技能/效果进入 `EffectProposalProcessingSystem` 的 Collect 阶段后，系统扫描所有匹配的 `ResponseChainListener`（`ResponseType=PromptInput`）。若持有该 Listener 的玩家存在可激活的反制手段，系统创建 `OrderRequest` 并将 `ResponseChainUiState.Visible` 置为 `true`，UI 显示选项（通过 / 无效化 / 激活陷阱）。玩家在 `GateDeadline` 内作出决策；若未输入则自动通过（Pass）。

与 O5（Hook/Cancel）的区别：O1 强调**玩家主动决策**，需要弹出 UI 提示；O5 的 Hook 是**程序自动取消**，无玩家交互。与 O8 的区别：O1 必有人工选择步骤，O8 的超时是该步骤无响应时的兜底。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`（反制技能本身的快捷键）
- **SelectionType**: `None`（陷阱目标锁定在触发者，不需额外选择）
- **InteractionMode**: `SmartCast`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/trap_counter.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "ability_trap",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "selectionType": "None",
  "isSkillMapping": true,
  "castModeOverride": null
}
```

---

## Graph 实现

```
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/trap_counter_effect.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// --- ResponseChainListener 配置 (挂在持有陷阱的单位上) ---
// eventTagId:    damage_incoming          // 监听入伤事件
// responseType:  PromptInput             // 弹出 UI，等待玩家决策
// priority:      10                      // 越低越先收到窗口

// --- Phase OnPropose Listener (priority=10, scope=Target) ---
// 在 PromptInput 决策已完成后，检查玩家选择：
Phase OnPropose Listener (priority=10, scope=Target):
  ReadBlackboardInt     E[effect], PlayerChoice    → I[0]
  // PlayerChoice: 0=Pass, 1=Negate, 2=ActivateTrap
  ConstInt              2                          → I[1]
  CompareEqInt          I[0], I[1]                 → B[0]
  JumpIfFalse           B[0], @check_negate
@activate_trap:
  LoadContextTarget     E[1]                       // 持有陷阱的单位
  LoadContextSource     E[0]                       // 发出伤害的单位
  ApplyEffectTemplate   "Effect.TrapCounter.Main"
  Jump                  @end
@check_negate:
  ConstInt              1                          → I[2]
  CompareEqInt          I[0], I[2]                 → B[1]
  JumpIfFalse           B[1], @end
  // Negate 分支：Hook 原效果（通过 SkipMain 标志，见 O5）
  WriteBlackboardInt    E[effect], SkipMain, 1
@end:

// --- Phase OnApply Main (陷阱效果主体) ---
Phase OnApply Main:
  LoadContextSource     E[0]             // 施法者（触发陷阱的单位）
  LoadContextTarget     E[1]             // 被保护单位
  // 示例：护盾 = 持有者基础护甲 * 系数
  LoadAttribute         E[1], Armor      → F[0]
  LoadConfigFloat       "ShieldCoeff"   → F[1]
  MulFloat              F[0], F[1]       → F[2]
  WriteBlackboardFloat  E[effect], ShieldAmount, F[2]
```

Effect 模板示例：
```json5
{
  "id": "Effect.TrapCounter.Main",
  "presetType": "Shield",
  "lifetime": { "kind": "Duration", "ticks": 180 },
  "configParams": {
    "ShieldCoeff": 2.0
  },
  "grantedTags": ["Status.Shielded"],
  "phaseListeners": []
}
```

响应窗口 `OrderRequest` 由 `EffectProposalProcessingSystem` 在 WindowPhase.WaitInput 阶段自动创建；UI 层监听 `ResponseChainUiState.Visible` 驱动弹窗。

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| EffectProposalProcessingSystem | `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` | ✅ 已有 |
| ResponseChainListener | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| ResponseType.PromptInput | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| WindowPhase (Collect/WaitInput/Resolve) | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| OrderRequest / OrderRequestQueue | `src/Core/Input/Orders/OrderRequestComponents.cs` | ✅ 已有 |
| ResponseChainUiState | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| ResponseChainHumanOrderSourceSystem | `src/Core/Input/Interaction/ResponseChainHumanOrderSourceSystem.cs` | ✅ 已有 |
| GateDeadline | `src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs` | ✅ 已有 |
| BlackboardIntBuffer | `src/Core/Gameplay/GAS/Components/BlackboardComponents.cs` | ✅ 已有 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | ✅ 已有 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

---

## 新增需求

无。现有基建可完整表达。

---

## 最佳实践

- **DO**: 将 `ResponseType=PromptInput` 的 Listener 优先级设为小于 50，确保在其他 Hook/Modify Listener 之前让玩家先做决策。
- **DO**: `PlayerChoice` Blackboard key 由 `ResponseChainHumanOrderSourceSystem` 写入，不要在 Graph 内直接读取原始输入状态。
- **DO**: 陷阱逻辑（护盾/反制数值）放在 `OnApply` 而非 `OnPropose`，`OnPropose` 只负责决策分支。
- **DON'T**: 不要在 `OnPropose` Graph 里直接调用 `ApplyEffectTemplate` 创建永久状态，若玩家选 Pass，副作用将无法撤回。
- **DON'T**: 不要用 `DoubleTap` 触发陷阱激活（已废弃）。
- **DON'T**: CC Tag（Status.Stunned 等）不能由 Graph 直接写位，必须通过 `GrantedTags` + Effect 生命周期管理。

---

## 验收口径

### 场景 1: 玩家在时限内选择激活陷阱

| 项 | 内容 |
|----|------|
| **输入** | 敌方施放 `Ability.Fireball` 命中己方单位；玩家在 3 秒内按 `1` 激活陷阱；持有单位 Armor=100 |
| **预期输出** | 己方单位获得护盾 `ShieldAmount = 100 * 2.0 = 200`；Fireball 伤害正常结算后护盾吸收 |
| **Log 关键字** | `[GAS] ResponseWindow Opened uid=<uid>` → `[GAS] PlayerChoice=2` → `[GAS] ApplyEffect Effect.TrapCounter.Main` |
| **截图要求** | 护盾量条出现在单位头顶，数值与公式一致 |
| **多帧录屏** | F0: Fireball OnPropose → F1: UI 弹出 → F60: 玩家按 1 → F61: OnPropose Listener 执行 → F62: TrapCounter OnApply → F63: 护盾出现 |

### 场景 2: 玩家选择 Negate（无效化对方效果）

| 项 | 内容 |
|----|------|
| **输入** | 同上，玩家按 `N` 选择 Negate |
| **预期输出** | `SkipMain=1` 写入 Blackboard；Fireball 不造成伤害；无护盾效果 |
| **Log 关键字** | `[GAS] PlayerChoice=1` → `[GAS] EffectSkipped SkipMain=1 effectId=<fireball_id>` |
| **截图要求** | 无伤害浮字，无护盾显示 |
| **多帧录屏** | F0: Fireball 提案 → F1: UI 弹窗 → F30: 按 N → F31: SkipMain 写入 → F32: Effect 跳过 |

### 场景 3: 超时自动通过（Pass）

| 项 | 内容 |
|----|------|
| **输入** | 敌方 Fireball 提案；玩家不操作；等待 `GateDeadline` 到期 |
| **预期输出** | 系统自动写入 `PlayerChoice=0`；Fireball 正常造成伤害；无陷阱激活 |
| **Log 关键字** | `[GAS] ResponseWindow Timeout uid=<uid>` → `[GAS] AutoPass PlayerChoice=0` |
| **截图要求** | 正常伤害浮字，无陷阱激活动画 |
| **多帧录屏** | F0: 提案 → F180: GateDeadline 到期 → F181: AutoPass → F182: 正常 OnApply |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/TrapCounterTests.cs
[Test]
public void O1_ActivateTrap_ShieldApplied()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500, armor: 100);
    AttachResponseChainListener(world, defender, eventTag: "damage_incoming",
        responseType: ResponseType.PromptInput, priority: 10);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.Fireball");
    world.Tick(1);  // OnPropose — ResponseWindow 打开
    // 模拟玩家选择激活陷阱
    InjectPlayerChoice(world, defender, choice: 2);
    world.Tick(2);  // OnPropose Listener 执行 → TrapCounter OnApply

    // Assert
    Assert.AreEqual(200f, GetBlackboard(world, defender, "ShieldAmount")); // 100 * 2.0
    Assert.IsTrue(HasTag(world, defender, "Status.Shielded"));
}

[Test]
public void O1_NegateEffect_DamageSkipped()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500, armor: 100);
    AttachResponseChainListener(world, defender, eventTag: "damage_incoming",
        responseType: ResponseType.PromptInput, priority: 10);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.Fireball");
    world.Tick(1);
    InjectPlayerChoice(world, defender, choice: 1); // Negate
    world.Tick(2);

    // Assert
    Assert.AreEqual(500, GetAttribute(world, defender, "Health")); // 不扣血
    Assert.AreEqual(1, GetBlackboard(world, defender, "SkipMain"));
}

[Test]
public void O1_Timeout_AutoPass_DamageDealt()
{
    // Arrange
    var world = CreateTestWorld();
    var attacker = SpawnUnit(world, baseDamage: 100);
    var defender = SpawnUnit(world, hp: 500, armor: 100);
    AttachResponseChainListener(world, defender, eventTag: "damage_incoming",
        responseType: ResponseType.PromptInput, priority: 10);
    SetGateDeadline(world, defender, ticks: 180);

    // Act
    world.SubmitOrder(attacker, OrderType.CastAbility, defender, abilityId: "Ability.Fireball");
    world.Tick(181); // 超过 GateDeadline

    // Assert — AutoPass，正常扣血
    Assert.Less(GetAttribute(world, defender, "Health"), 500f);
}
```

### 集成验收
1. 运行 `dotnet test --filter "O1"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/response_window/o1_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/response_window/o1_normal.png`
   - `artifacts/acceptance/response_window/o1_edge.png`
4. 多帧录屏：
   - `artifacts/acceptance/response_window/o1_60frames.gif`
   - 标注关键帧：OnPropose 帧、UI 弹窗帧、玩家决策帧、OnApply 帧

---

## 参考案例

- **游戏王 陷阱卡**: 对方卡片进入结算链时，弹出 "是否激活" 提示，时限内可激活陷阱牌插入连锁。
- **LoL Sivir E（法术盾）**: 监听敌方技能命中事件，弹窗提示玩家手动激活或依赖自动检测。
- **Dota Linken's Sphere**: 自动 Hook 单体指向技能（ResponseType.Hook），无 UI 交互，与 O1 不同（见 O5）。
