# C3: 任意单位，效果随阵营关系变化

> 对显式选中的单位施放同一个技能。如果目标是敌方，则命中敌方分支；如果目标是友方，则命中友方分支。  
> 当前 `InteractionShowcaseMod` 的验收实现没有引入新的 Core graph 条件节点，也没有做 showcase-only 的并行运行时，而是复用标准 GAS 能力/效果配置：`Ability.Interaction.C3AnyUnitConditional` 在 `tick=0` 同时发出两个 `Search` wrapper effect，再由 fan-out 路径上的 `targetFilter.relationFilter` 决定哪个 payload effect 真正落到显式目标上。  
> 对齐代码：`mods/InteractionShowcaseMod/assets/GAS/abilities.json`、`mods/InteractionShowcaseMod/assets/GAS/effects.json`、`mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs`

---

## 机制说明

玩家意图是“把同一个显式目标技能，对敌方和友方打出不同结果”。在生产交互合同里，这类技能仍然属于：

- `trigger = PressedThisFrame`
- `selectionType = Entity`
- `orderTypeKey = castAbility`

当前 showcase 为了保证确定性，不走实时鼠标选取，而是由 `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` 直接向 `OrderQueue` 提交固定目标：

1. 首次施放命中敌方目标 `C3EnemyPrimary`
2. 观察敌方分支 `Effect.Interaction.C3HostilePolymorph`
3. 第二次施放命中友方目标 `C3AllyPrimary`
4. 观察友方分支 `Effect.Interaction.C3FriendlyHaste`

因此本文档描述的是“该交互能力的真实 GAS 表达方式 + 当前 showcase 的确定性验收落地”，不是额外造出来的一套 show-only 逻辑。

---

## 为什么没有使用最初的 graph 条件分支方案

最初设计稿假设可以在 effect graph 中直接做：

1. 读取 source / target 阵营
2. 求 relationship
3. 根据 relationship 走 hostile / friendly 分支

但当前仓库的 JSON graph 配置层并没有暴露这个能力：

- `IGraphRuntimeApi` 虽然有 `GetRelationship(...)`
- 但 `mods/.../assets/GAS/graphs.json` 可用的 JSON node / op 并没有公开一个可直接配置的 `GetRelationship` / `GetTeamId` 节点
- 因此不能把 C3 伪装成“已有 graph 条件分支能力”

另外，在真正落地时还发现一个更底层的事实：

- `targetFilter.relationFilter` 对 **direct explicit-target effect** 不生效
- 它只在 `targetQuery + targetDispatch` 的 fan-out 路径里被真正执行

对应技术债：

- `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap`
- `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md`

因此 C3 当前采用的是真实、可复用、且不掩盖缺口的方案：

- 同一 ability
- 同时发出两个 `Search` wrapper effect
- 两个 wrapper 都围绕显式目标做一个极小半径的 circle query
- hostile wrapper 用 `relationFilter = Hostile`
- friendly wrapper 用 `relationFilter = Friendly`
- 命中的 wrapper 再 `targetDispatch` 到对应 payload effect

---

## Showcase 场景卡

- 地图：`mods/InteractionShowcaseMod/assets/Maps/interaction_c3_any_unit_conditional.json`
- Ability：`Ability.Interaction.C3AnyUnitConditional`
- Search wrapper：
  - `Effect.Interaction.C3HostileConditionalSearch`
  - `Effect.Interaction.C3FriendlyConditionalSearch`
- Payload effect：
  - `Effect.Interaction.C3HostilePolymorph`
  - `Effect.Interaction.C3FriendlyHaste`
- Hero 模板：`interaction_c3_hero`
- 目标模板：
  - `interaction_c3_target_hostile`
  - `interaction_c3_target_friendly`
- 演示实体：
  - `ArpgHero`
  - `C3EnemyPrimary`
  - `C3AllyPrimary`

初始数值：

- Hero: `Health = 1000`, `Mana = 100`, `MoveSpeed = 220`
- Hostile target: `Health = 500`, `MoveSpeed = 200`
- Friendly target: `Health = 500`, `MoveSpeed = 180`

成功结果：

- Hostile target:
  - `MoveSpeed = 200 -> 80`
  - 获得 `Status.Polymorphed`
- Friendly target:
  - `MoveSpeed = 180 -> 260`
  - 获得 `Status.Hasted`

---

## 实现链路

### Ability

当前能力配置位于 `mods/InteractionShowcaseMod/assets/GAS/abilities.json`：

```json5
{
  "id": "Ability.Interaction.C3AnyUnitConditional",
  "exec": {
    "clockId": "FixedFrame",
    "items": [
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.Interaction.C3HostileConditionalSearch"
      },
      {
        "kind": "EffectSignal",
        "tick": 0,
        "template": "Effect.Interaction.C3FriendlyConditionalSearch"
      },
      {
        "kind": "End",
        "tick": 0
      }
    ]
  }
}
```

### Search Wrapper Effects

当前 wrapper effect 配置位于 `mods/InteractionShowcaseMod/assets/GAS/effects.json`：

```json5
{
  "id": "Effect.Interaction.C3HostileConditionalSearch",
  "presetType": "Search",
  "lifetime": "Instant",
  "targetQuery": {
    "kind": "BuiltinSpatial",
    "shape": "Circle",
    "radius": 80
  },
  "targetFilter": {
    "relationFilter": "Hostile",
    "maxTargets": 1
  },
  "targetDispatch": {
    "payloadEffect": "Effect.Interaction.C3HostilePolymorph"
  }
}
```

```json5
{
  "id": "Effect.Interaction.C3FriendlyConditionalSearch",
  "presetType": "Search",
  "lifetime": "Instant",
  "targetQuery": {
    "kind": "BuiltinSpatial",
    "shape": "Circle",
    "radius": 80
  },
  "targetFilter": {
    "relationFilter": "Friendly",
    "maxTargets": 1
  },
  "targetDispatch": {
    "payloadEffect": "Effect.Interaction.C3FriendlyHaste"
  }
}
```

关键点：

- `Circle` 查询默认以显式目标 `ctx.Target` 为圆心
- `relationFilter` 在 fan-out 路径生效
- 只有通过 filter 的 wrapper 才会 dispatch payload effect

### Payload Effects

```json5
{
  "id": "Effect.Interaction.C3HostilePolymorph",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 300 },
  "stackPolicy": { "policy": "Replace" },
  "modifiers": [
    { "attribute": "MoveSpeed", "op": "Add", "value": -120.0 }
  ],
  "grantedTags": [
    { "tag": "Status.Polymorphed", "formula": "Fixed", "amount": 1 }
  ]
}
```

```json5
{
  "id": "Effect.Interaction.C3FriendlyHaste",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 300 },
  "stackPolicy": { "policy": "Replace" },
  "modifiers": [
    { "attribute": "MoveSpeed", "op": "Add", "value": 80.0 }
  ],
  "grantedTags": [
    { "tag": "Status.Hasted", "formula": "Fixed", "amount": 1 }
  ]
}
```

这里没有新增 graph 条件节点，也没有在 showcase system 里手写“如果敌方就减速、如果友方就加速”。真正的关系判断发生在标准 `Search` fan-out 过滤阶段。

---

## Validation Scope

- 已验证：
  - 同一 ability
  - 同一显式目标交互合同
  - hostile / friendly 两条正向分支都走真实 GAS 管线
  - 关系选择由 native fan-out `relationFilter` 完成
- 未宣称：
  - direct explicit-target payload effect 自身会正确 honor `targetFilter.relationFilter`
- 当前 fuse：
  - C3 通过 `Search + targetDispatch payload` 进行隔离式落地
  - 不在 feature 任务里偷偷补 Core

---

## 依赖复用

| 能力/组件 | 路径 | 状态 |
|------|------|------|
| `AbilityDefinitionRegistry` | `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs` | 已复用 |
| `EffectTemplateRegistry` | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | 已复用 |
| `TargetResolverFanOutHelper` | `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs` | 已复用 |
| `EffectPresetType.Search` | `assets/Configs/GAS/preset_types.json` | 已复用 |
| `EffectPresetType.Buff` | `assets/Configs/GAS/preset_types.json` | 已复用 |
| `AbilityExecSystem` | `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` | 已复用 |
| `EffectProcessingLoopSystem` | `src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs` | 已复用 |
| `OrderQueue` | `src/Core/Gameplay/GAS/Orders/` | 已复用 |
| `Showcase autoplay` | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseAutoplaySystem.cs` | 演示专用确定性驱动 |
| `Overlay / visual debug` | `mods/InteractionShowcaseMod/Systems/InteractionShowcaseOverlaySystem.cs` | 演示专用 |
| `Launcher recorder` | `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs` | 视觉证据导出 |

---

## 验收口径

### 场景 1：对敌方显式目标命中 hostile 分支

| 项目 | 内容 |
|----|------|
| 输入 | Hero 对 `C3EnemyPrimary` 发起 slot `0` 的 `castAbility` |
| 预期输出 | `HostileTarget.MoveSpeed = 80`，获得 `Status.Polymorphed` |
| 代码证据 | `mods/InteractionShowcaseMod/assets/GAS/abilities.json`、`mods/InteractionShowcaseMod/assets/GAS/effects.json`、`src/Tests/GasTests/C3AnyUnitConditionalTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/002_hostile_polymorph_applied.png` |

### 场景 2：对友方显式目标命中 friendly 分支

| 项目 | 内容 |
|----|------|
| 输入 | Hero 对 `C3AllyPrimary` 再次发起同一 slot `0` 技能 |
| 预期输出 | `FriendlyTarget.MoveSpeed = 260`，获得 `Status.Hasted` |
| 代码证据 | `mods/InteractionShowcaseMod/assets/GAS/abilities.json`、`mods/InteractionShowcaseMod/assets/GAS/effects.json`、`src/Tests/GasTests/C3AnyUnitConditionalTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/004_friendly_haste_applied.png` |

### 场景 3：同一技能不会把错误分支交叉打到另一方

| 项目 | 内容 |
|----|------|
| 输入 | 复用上面两次施放 |
| 预期输出 | hostile capture 时友方不获得 `Haste`；friendly capture 时敌方仍保持 `Polymorph`，没有被错误覆盖 |
| 代码证据 | `src/Tests/GasTests/C3AnyUnitConditionalTests.cs`、`src/Tests/GasTests/Production/C3AnyUnitConditionalAcceptanceTests.cs` |
| 视觉证据 | `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/timeline.png` |

---

## 测试与证据

### Headless 测试

- 断言测试：`src/Tests/GasTests/C3AnyUnitConditionalTests.cs`
- 产物测试：`src/Tests/GasTests/Production/C3AnyUnitConditionalAcceptanceTests.cs`

建议命令：

```powershell
dotnet test .\src\Tests\GasTests\GasTests.csproj -c Release --filter C3AnyUnitConditional
```

### 视觉录制

- 录制脚本：`scripts/record-interaction-c3-any-unit-conditional.ps1`
- 二审脚本：`scripts/review-interaction-c3-any-unit-conditional.ps1`
- Launcher 启动脚本：`scripts/run-mod-launcher.cmd`
- 当前 showcase startup map 配置：`mods/InteractionShowcaseMod/assets/game.json`

录制产物：

- `artifacts/acceptance/interaction-c3-any-unit-conditional/battle-report.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/trace.jsonl`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/path.mmd`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/battle-report.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/trace.jsonl`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/path.mmd`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/summary.json`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/screens/*.png`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/interaction-c3-any-unit-conditional.mp4`
- `artifacts/acceptance/interaction-c3-any-unit-conditional/visual/interaction-c3-any-unit-conditional.gif`

---

## 最佳实践

- **DO**: 把关系分支放在原生 fan-out `Search + targetDispatch` 里，而不是在 showcase system 里手写 if/else
- **DO**: 明确区分“当前真实可用的配置路径”和“最初文档中尚未实现的 graph 方案”
- **DO**: 在文档和验收报告里公开 direct explicit-target `relationFilter` gap，而不是假装它已被 Core 支持
- **DON'T**: 不要把当前 C3 说成“已有 graph 条件分支节点能力”的证明
- **DON'T**: 不要把 direct explicit-target `targetFilter.relationFilter` 写成已验证能力；当前已知它不成立
