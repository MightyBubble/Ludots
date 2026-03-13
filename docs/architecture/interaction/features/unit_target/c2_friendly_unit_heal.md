# C2: 友方单位治疗

> 对明确选中的友方单位施放即时治疗。当前 `InteractionShowcaseMod` 的验收实现复用了标准 GAS 执行链：`Ability.Interaction.C2FriendlyUnitHeal` 在 `tick=0` 发出 `Effect.Interaction.C2FriendlyUnitHeal`，该 Effect 直接使用内建 `Heal` preset，并通过 `Health +150` modifier 完成治疗。注意：`targetFilter.relationFilter = Friendly` 虽然仍配置在 effect 上，但 `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` 已明确 direct explicit-target 路径不能被当作已证明可靠的原生 relation fence；本 showcase 的 hostile / dead-ally 负向收口仍以 showcase-local validation 为准。
> 对齐代码：`mods/InteractionShowcaseMod/assets/GAS/abilities.json`、`mods/InteractionShowcaseMod/assets/GAS/effects.json`、`mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`。

---

## 机制说明

玩家意图是“对一个受伤友军释放单体治疗”。在生产交互合同里，这类技能仍属于：

- `trigger = PressedThisFrame`
- `selectionType = Entity`
- `orderTypeKey = castAbility`

但当前 showcase 为了保证确定性，不走实时鼠标选取，而是由 `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` 直接向 `OrderQueue` 提交固定目标：

- 首次施放命中 `C2AllyPrimary`
- 第二次尝试命中敌方单位 `C2EnemyInvalid`，预期 `InvalidTarget`
- 第三次尝试命中死亡友军 `C2AllyDead`，预期 `InvalidTarget`

因此本文档描述的是“该交互能力的标准 GAS 表达方式 + 当前 showcase 的确定性验收落地”，不是额外平行的一套运行时。

---

## Showcase 场景卡

- 地图：`mods/InteractionShowcaseMod/assets/Maps/interaction_c2_friendly_unit_heal.json`
- Ability：`Ability.Interaction.C2FriendlyUnitHeal`
- Effect：`Effect.Interaction.C2FriendlyUnitHeal`
- Hero 模板：`interaction_c2_hero`
- 目标模板：
  - `interaction_c2_target_ally`
  - `interaction_c2_target_hostile`
  - `interaction_c2_target_dead_ally`
- 演示实体：
  - `ArpgHero`
  - `C2AllyPrimary`
  - `C2EnemyInvalid`
  - `C2AllyDead`

模板 base 数值：

- Hero: `Health = 1000`, `Mana = 100`
- Ally target: `Health = 500`
- Hostile target: `Health = 400`
- Dead ally target: `Health = 500`

Showcase warmup 当前值：

- Hero: `Mana = 100`
- Ally target: `Current Health = 200`
- Hostile target: `Current Health = 400`
- Dead ally target: `Current Health = 0`
- 资源口径：当前 showcase 未配置 mana cost，因此 Hero `Mana` 全程保持 `100`

成功结果：

- Ally target current Health: `200 -> 350`
- `HealAmount = 150`

---

## 实现链路

### Ability

当前能力配置位于 `mods/InteractionShowcaseMod/assets/GAS/abilities.json`：

```json5
{
  "id": "Ability.Interaction.C2FriendlyUnitHeal",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.Interaction.C2FriendlyUnitHeal"
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
  "id": "Effect.Interaction.C2FriendlyUnitHeal",
  "tags": [
    "Effect.Interaction.C2FriendlyUnitHeal"
  ],
  "presetType": "Heal",
  "lifetime": "Instant",
  "targetFilter": {
    "relationFilter": "Friendly"
  },
  "modifiers": [
    {
      "attribute": "Health",
      "op": "Add",
      "value": 150.0
    }
  ]
}
```

这里没有单独的 graph，也没有 showcase 专用治疗系统。治疗值完全来自内建 `Heal` preset 的标准 modifier 执行。

### Showcase Autoplay

当前确定性验收由 `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` 驱动：

1. warmup 阶段把 ally / hostile / dead-ally 当前 Health 设为 `200 / 400 / 0`，但不改模板 base 值
2. 向 `OrderQueue` 提交 slot `0` 的友方治疗
3. 监听 ally Health 到达 `350`，记录 `HealAmount = 150`
4. 对 hostile 与 dead ally 进行两次额外尝试，记录 `InvalidTarget`

关键对齐点：

- 正向分支是实打实的 `OrderQueue -> AbilityExecSystem -> EffectProcessingLoopSystem`
- 负向分支当前由 autoplay 的 `IsValidC2Target(...)` 做本地校验
- hostile 分支的 `targetFilter.relationFilter = Friendly` 目前不能被当作 direct explicit-target 路径上的可靠原生 relation fence；`artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md` 已记录该缺口，因此本 showcase 明确把 hostile 拒绝口径定义为 showcase-local pre-enqueue validation，而不是“有 config 所以原生一定会挡住”
- `heal_applied_tick` / `002_heal_applied` 表示“AutoplaySystem 首次发布或观察到完成态”的 tick，不应被解释成精确的 GAS modifier 提交时刻；headless trace 可能已经在 `order_submitted` 末段看到 `allyHP=350`，而 visual trace 只在下一检查点展示 `heal_applied`
- dead-ally 分支没有已验证的原生 GAS alive-status fence；如果本地 alive 校验被绕过，`Friendly + Heal` 会命中死亡友军并把 `Health 0 -> 150`
- 因此当前证据证明了“showcase 交互合同成立”，但**不证明** hostile / dead-ally 分支一定来自 GAS 原生 `CastFailed` 事件

---

## Validation Scope

- 正向分支：已验证 `OrderQueue -> AbilityExecSystem -> EffectProcessingLoopSystem -> Heal preset` 的原生 GAS 链路。
- hostile 分支：当前**先**由 showcase-local validation 拦截；effect 上仍配置了 `relationFilter = Friendly`，但 direct explicit-target relation-filter gap 已由 C3 技术债确认，因此这里明确不把它写成“存在即可依赖”的 GAS 原生 hostile fence。
- dead-ally 分支：当前**仅**由 showcase-local `health > 0` guard 拦截；现有 `Friendly + Heal` 配置没有已验证的原生 alive-status fence。
- 技术债与 fuse：`artifacts/techdebt/2026-03-13-c2-dead-ally-alive-fence-gap.md`。当前 fuse 模式为 `isolation`，即在 showcase 层显式阻断 dead-ally 提交，并在证据产物中公开记录该边界，而不是静默假装 Core 已支持死亡目标拒绝。

---

## 依赖复用

| 能力/组件 | 路径 | 状态 |
|------|------|------|
| `AbilityDefinitionRegistry` | `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs` | 已复用 |
| `EffectTemplateRegistry` | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | 已复用 |
| `EffectPresetType.Heal` | `assets/Configs/GAS/preset_types.json` | 已复用 |
| `AbilityExecSystem` | `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` | 已复用 |
| `EffectProcessingLoopSystem` | `src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs` | 已复用 |
| `OrderQueue` | `src/Core/Gameplay/GAS/Orders/` | 已复用 |
| `Input order contract` | `src/Core/Input/Orders/InputOrderMapping.cs` | 交互合同仍适用 |
| `Showcase autoplay` | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` | 演示专用确定性驱动 |
| `Overlay / visual debug` | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseOverlaySystem.cs` | 演示专用 |
| `Launcher recorder` | `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs` | 视觉证据导出 |

---

## 验收口径

### 场景 1：治疗受伤友军

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C2AllyPrimary` 发起 slot `0` 的 `castAbility` |
| 预期输出 | `AllyTarget.Health = 350`，`HealAmount = 150` |
| 代码证据 | `mods/InteractionShowcaseMod/assets/GAS/effects.json`、`mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`、`src/Tests/GasTests/C2FriendlyUnitHealTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/002_heal_applied.png` |

### 场景 2：敌方单位不可作为治疗目标

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C2EnemyInvalid` 再次发起同一技能 |
| 预期输出 | 当前 showcase 在本地校验阶段阻止入队；失败原因为 `InvalidTarget`；`HostileTarget.Health` 维持 `400` |
| 代码证据 | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`、`src/Tests/GasTests/C2FriendlyUnitHealTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/003_hostile_target_blocked.png` |

### 场景 3：死亡友军不可作为治疗目标

| 项 | 内容 |
|----|------|
| 输入 | Hero 对 `C2AllyDead` 再次发起同一技能 |
| 预期输出 | 当前 showcase 仅在本地 alive guard 阶段阻止入队；失败原因为 `InvalidTarget`；`DeadAllyTarget.Health` 维持 `0` |
| 代码证据 | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`、`src/Tests/GasTests/C2FriendlyUnitHealTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/004_dead_ally_blocked.png` |

---

## 测试与证据

### Headless 测试

- 断言测试：`src/Tests/GasTests/C2FriendlyUnitHealTests.cs`
- 产物测试：`src/Tests/GasTests/Production/C2FriendlyUnitHealAcceptanceTests.cs`
- root `trace.jsonl` 从 `tick=0` 开始，前两条样本仍会看到 warmup 前的模板 base 值；真正的 showcase warmup 从 `tick=1` 开始

建议命令：

```powershell
dotnet test .\src\Tests\GasTests\GasTests.csproj -c Release --filter C2FriendlyUnitHeal
```

### 视觉录制

- 录制脚本：`scripts/record-interaction-c2-friendly-unit-heal.ps1`
- 二审脚本：`scripts/review-interaction-c2-friendly-unit-heal.ps1`
- Launcher 启动脚本：`scripts/run-mod-launcher.cmd`
- 当前 showcase startup map 配置：`mods/InteractionShowcaseMod/assets/game.json`

录制产物：

- `artifacts/acceptance/interaction-c2-friendly-unit-heal/battle-report.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/trace.jsonl`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/path.mmd`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/battle-report.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/trace.jsonl`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/path.mmd`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/summary.json`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/screens/*.png`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/interaction-c2-friendly-unit-heal.mp4`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/interaction-c2-friendly-unit-heal.gif`

---

## 最佳实践

- **DO**: 让治疗值通过标准 `Heal` preset + modifier 表达，而不是在 showcase system 里硬写 `+150`
- **DO**: 用真实 `OrderQueue` 和 GAS 执行链跑正向分支，不要造第二套 show-only 治疗运行时
- **DO**: 在文档和验收报告里明确标注负向分支目前是 showcase-local validation
- **DO**: 区分“GAS 在提交帧内完成治疗”和“Autoplay/Recorder 在下一帧首次观察到治疗结果”这两种 tick 语义
- **DON'T**: 不要把 hostile / dead-ally 的 `InvalidTarget` 误写成已经由 GAS 原生失败事件完全证明
- **DON'T**: 不要假设 `Friendly` filter 会阻止死亡友军被治疗；当前 alive gate 来自 showcase-local validation
- **DON'T**: 不要把 slot 写成 `1`；当前 showcase 使用的是 slot `0`
