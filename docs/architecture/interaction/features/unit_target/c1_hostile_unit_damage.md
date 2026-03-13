# C1: 敌方单位伤害

> 对单一敌方单位造成即时伤害。当前 `InteractionShowcaseMod` 的演示实现复用了标准 GAS 执行链：`OnCalculate` 计算理论伤害并写入目标 blackboard，`OnApply.pre` 读取目标护甲做减伤并直接扣减 `Health`。  
> 对齐代码：`mods/InteractionShowcaseMod/assets/GAS/abilities.json`、`mods/InteractionShowcaseMod/assets/GAS/effects.json`、`mods/InteractionShowcaseMod/assets/GAS/graphs.json`、`mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`。

---

## 机制描述

玩家意图是“把一个即时单体伤害技能打到敌方单位身上”。在生产交互合同里，这类技能仍然属于：

- `trigger = PressedThisFrame`
- `selectionType = Entity`
- `orderTypeKey = castAbility`

但当前验收演示为了保证确定性，不走实时鼠标选取和通用自动寻敌，而是由 `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` 直接向 `OrderQueue` 提交固定目标：

- 首次施放命中 `C1EnemyPrimary`
- 第二次尝试命中死亡目标 `C1EnemyInvalid`，预期 `InvalidTarget`
- 第三次尝试命中超射程目标 `C1EnemyFar`，预期 `OutOfRange`

当前 C1 的两个负向分支都由 showcase autoplay 在提交前做本地校验：死亡目标不会进入 `OrderQueue`，超射程目标也不会入队。它们证明的是“showcase 交互合同的负向保护”，不是 GAS 原生 `CastFailed` fence。

这意味着本页描述的是“该交互能力的标准 GAS 表达方式 + 当前 showcase 的确定性验收落地”，不是一个额外的平行运行时。

---

## Showcase 场景卡

- 地图：`mods/InteractionShowcaseMod/assets/Maps/interaction_c1_hostile_unit_damage.json`
- 角色模板：`mods/InteractionShowcaseMod/assets/Entities/templates.json`
- 技能：`Ability.Interaction.C1HostileUnitDamage`
- Effect：`Effect.Interaction.C1HostileUnitDamage`
- Graph：
  - `Graph.Interaction.C1.CalculateDamage`
  - `Graph.Interaction.C1.ApplyMitigatedDamage`
- 演示实体：
  - `ArpgHero`
  - `C1EnemyPrimary`
  - `C1EnemyInvalid`
  - `C1EnemyFar`

初始数值：

- Hero: `BaseDamage = 200`, `Mana = 100`
- Primary target: `Health = 500`, `Armor = 50`
- Invalid target: `Health = 0`
- Far target: `Health = 500`

---

## 实现链路

### Ability

当前能力配置位于 `mods/InteractionShowcaseMod/assets/GAS/abilities.json`：

```json5
{
  "id": "Ability.Interaction.C1HostileUnitDamage",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.Interaction.C1HostileUnitDamage"
      },
      {
        "kind": "End",
        "tick": 0
      }
    ]
  }
}
```

### Effect

当前 effect 配置位于 `mods/InteractionShowcaseMod/assets/GAS/effects.json`：

```json5
{
  "id": "Effect.Interaction.C1HostileUnitDamage",
  "presetType": "None",
  "lifetime": "Instant",
  "configParams": {
    "Interaction.C1.DamageCoeff": {
      "type": "float",
      "value": 1.5
    }
  },
  "phaseGraphs": {
    "OnCalculate": {
      "pre": "Graph.Interaction.C1.CalculateDamage"
    },
    "OnApply": {
      "pre": "Graph.Interaction.C1.ApplyMitigatedDamage"
    }
  }
}
```

这里没有 `phaseListeners`，也没有额外的 Effect entity blackboard。当前实现直接在 phase graph 中完成：

1. `OnCalculate.pre` 写入目标 blackboard `Interaction.C1.DamageAmount`
2. `OnApply.pre` 读取目标 blackboard 与 `Armor`
3. 同一 graph 写回 `Interaction.C1.FinalDamage`
4. 直接调用 `ModifyAttributeAdd` 扣减目标 `Health`

当前 effect/graph 本身没有 `targetFilter.relationFilter`、alive fence 或 range fence。也就是说，如果绕过 showcase-local `TrySubmitC1Cast(...)`，死亡目标和超射程目标不会由同层配置自动拒绝；当前负向证据口径依旧是“本地 guard 生效”，不是“Core/GAS 已原生覆盖这两个 reject case”。

### Graph

当前 graph 配置位于 `mods/InteractionShowcaseMod/assets/GAS/graphs.json`：

```text
Graph.Interaction.C1.CalculateDamage
  LoadContextSource
  LoadContextTarget
  LoadAttribute(BaseDamage)
  LoadConfigFloat(Interaction.C1.DamageCoeff)
  MulFloat
  WriteBlackboardFloat(target, Interaction.C1.DamageAmount)

Graph.Interaction.C1.ApplyMitigatedDamage
  LoadContextTarget
  ReadBlackboardFloat(target, Interaction.C1.DamageAmount)
  LoadAttribute(Armor)
  ConstFloat(100)
  AddFloat
  DivFloat
  MulFloat
  WriteBlackboardFloat(target, Interaction.C1.FinalDamage)
  NegFloat
  ModifyAttributeAdd(target, Health)
```

关键对齐点：

- 这里的 blackboard owner 是 **target entity**，不是 effect entity。
- 原因是当前 graph runtime 通过 `src/Core/NodeLibraries/GASGraph/Host/GraphProgramLoader.cs` 和 `src/Core/NodeLibraries/GASGraph/Host/GraphProgramConfigLoader.cs` 暴露的是 source / target 上下文寄存器，showcase 没有再扩一层 effect-entity register。
- 因此验收与 battle-report 中出现的 `DamageAmount` / `FinalDamage` 都应该解释为“目标 blackboard 上的中间值”。

---

## 数学口径

当前演示数值固定为：

- `DamageAmount = BaseDamage * DamageCoeff = 200 * 1.5 = 300`
- `FinalDamage = DamageAmount * 100 / (100 + Armor) = 300 * 100 / 150 = 200`
- `PrimaryTarget.Health = 500 -> 300`

这些值由以下代码面共同约束：

- `mods/InteractionShowcaseMod/assets/Entities/templates.json`
- `mods/InteractionShowcaseMod/assets/GAS/effects.json`
- `mods/InteractionShowcaseMod/assets/GAS/graphs.json`
- `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`
- `src/Tests/GasTests/C1HostileUnitDamageTests.cs`

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| AbilityDefinitionRegistry | `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs` | 已复用 |
| EffectTemplateRegistry | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | 已复用 |
| EffectPhaseExecutor | `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` | 已复用 |
| AbilityExecSystem | `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` | 已复用 |
| EffectProcessingLoopSystem | `src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs` | 已复用 |
| BlackboardFloatBuffer | `src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs` | 已复用 |
| ConfigKeyRegistry | `src/Core/Gameplay/GAS/Registry/ConfigKeyRegistry.cs` | 已复用 |
| GraphOps | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | 已复用 |
| Input order contract | `src/Core/Input/Orders/InputOrderMapping.cs` | 交互合同仍适用 |
| Showcase autoplay | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` | 演示专用确定性驱动 |
| Overlay / visual debug | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseOverlaySystem.cs` | 演示专用 |
| Launcher recorder | `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs` | 视觉证据导出 |

---

## 验收口径

### 场景 1：命中有效敌方目标

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C1EnemyPrimary` 发起 slot `0` 的 `castAbility` |
| 预期输出 | `DamageAmount = 300`，`FinalDamage = 200`，`PrimaryTarget.Health = 300` |
| 代码证据 | `mods/InteractionShowcaseMod/assets/GAS/graphs.json`、`src/Tests/GasTests/C1HostileUnitDamageTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/002_damage_applied.png` |

### 场景 2：目标无效

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C1EnemyInvalid` 发起同一技能 |
| 预期输出 | 不入队实际伤害结算；失败原因 `InvalidTarget`；`InvalidTarget.Health` 维持 `0` |
| 代码证据 | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`、`src/Tests/GasTests/C1HostileUnitDamageTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/003_invalid_target_blocked.png` |

### 场景 3：超出射程

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C1EnemyFar` 发起同一技能 |
| 预期输出 | 不入队实际伤害结算；失败原因 `OutOfRange`；`FarTarget.Health` 维持 `500` |
| 代码证据 | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`、`src/Tests/GasTests/C1HostileUnitDamageTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/004_out_of_range_blocked.png` |

---

## 测试与证据

### Headless 测试

- 断言测试：`src/Tests/GasTests/C1HostileUnitDamageTests.cs`
- 产物测试：`src/Tests/GasTests/Production/C1HostileUnitDamageAcceptanceTests.cs`

建议命令：

```powershell
dotnet test .\src\Tests\GasTests\GasTests.csproj -c Release --filter C1HostileUnitDamage
```

### 视觉录制

- 录制脚本：`scripts/record-interaction-c1-hostile-unit-damage.ps1`
- 二审脚本：`scripts/review-interaction-c1-hostile-unit-damage.ps1`
- Launcher 启动脚本：`scripts/run-mod-launcher.cmd`
- 当前演示 startup map 配置：`mods/InteractionShowcaseMod/assets/game.json`

录制产物：

- `artifacts/acceptance/interaction-c1-hostile-unit-damage/battle-report.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/trace.jsonl`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/path.mmd`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/battle-report.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/summary.json`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/screens/*.png`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/interaction-c1-hostile-unit-damage.mp4`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/interaction-c1-hostile-unit-damage.gif`

---

## 最佳实践

- **DO**: 让伤害公式进入 graph，而不是把 `300` / `200` 这种数字写死在系统逻辑里。
- **DO**: 明确 blackboard owner；当前实现的 owner 是目标实体，不是 effect 实体。
- **DO**: 用真实 `OrderQueue`、`AbilityExecSystem`、`EffectProcessingLoopSystem` 跑验收，不造第二套 show-only 伤害管线。
- **DON'T**: 不要在 mod 层重复发明一个“简化伤害系统”来跑演示。
- **DON'T**: 不要在文档里把当前 `phaseGraphs.OnApply.pre` 写成 `phaseListeners`；那不是现在的实现。
- **DON'T**: 不要在文档、测试或 battle-report 中把 `DamageAmount` / `FinalDamage` 解释为 effect entity state；当前实现不是那样。
