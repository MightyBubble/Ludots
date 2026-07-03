# R6: Rally Point / Spawn Target

## 机制描述

设置 spawn target（集结点），新生产的单位在 spawn 时自动收到 Mod 配置的 order（如 `moveTo` 或 garrison `castAbility`）。

## 架构（当前实现）

业务语义在 **Mod 配置**；Core 提供通用基建（见 `gitbook/architecture/input-order-and-spawn-target.md`）。

```
input_order_mappings.json (Command)
  └─ actorOrderRouting
       ├─ match: producer slot 2 + ".Train" → orderTypeKey: setSpawnTarget
       │    selectionType: HoveredEntityOrPosition
       └─ default candidate → orderTypeKey: moveTo

order_types.json (Mod)
  └─ setSpawnTarget: instantComplete + persistentStoredTarget (Rts.SpawnTarget.*)

InstantCompleteOrderSystem
  └─ CommitFromOrder → building blackboard

CreateUnit onSpawnEffect
  └─ SubmitOrderFromBlackboard（Effect.Rts.Shared.ApplySpawnTargetOrder）
       └─ unitCreation.onSpawnEffect + copySourcePlayerOwner: true
       └─ spawned unit moveTo / castAbility（需 PlayerOwner）
```

## 前置条件

- `actorOrderRouting` mapping 须 `isSkillMapping: false`
- `TagOps` 在 mapping 创建期必须可用（RtsDemoMod 经 LudotsCoreMod 注册）
- Train CreateUnit effect 须设置 `unitCreation.onSpawnEffect` 指向 `SubmitOrderFromBlackboard` preset
- Spawn 出的单位须继承 `PlayerOwner`（`copySourcePlayerOwner: true`）

## 交互层

| 步骤 | 输入 | 结果 |
|------|------|------|
| 设点 rally | Command + 选中 producer + 点地 | `setSpawnTarget` → point stored target |
| 设 entity rally | Command + 选中 producer + 点单位 | `setSpawnTarget` → entity stored target |
| 普通移动 | Command + 选中战斗单位 + 点地 | `moveTo` |
| Spawn | 训练完成 | on-spawn effect 读 building blackboard 并下发 order |

## Mod 配置 SSOT（RtsDemoMod）

- `assets/GAS/order_types.json` — `setSpawnTarget`, `Rts.SpawnTarget.*`
- `assets/Input/input_order_mappings.json` — `actorOrderRouting`
- `assets/GAS/effects.json` — `Effect.Rts.Shared.ApplySpawnTargetOrder` + train `unitCreation.onSpawnEffect`

## 参考案例

- **StarCraft**: 右键设置集结点
- **Age of Empires**: 兵营集结点旗帜

## 跟踪

- GitHub: #492, #493
- Kanban: SY56 🟢, SY14 部分（本地选中 actor 路由）
