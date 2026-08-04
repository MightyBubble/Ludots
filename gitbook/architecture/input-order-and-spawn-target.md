# Input Order Routing 与 Spawn Target 基建

本页描述 R6 spawn target / rally 所依赖的正式 Core 基建。业务语义（RTS producer rally、garrison 等）由 Mod 配置，Core 只提供通用原语。

## 1 设计原则

- Core 不得硬编码 rally、producer、或具体 Mod 的 order type key
- 行为 SSOT 在 JSON：`order_types.json`、`input_order_mappings.json`、`effects.json`
- 禁止代码层 fallback：路由 unmatched actor 时跳过，不注入默认 order type

## 2 Input：`CommandIntent` + typed `orderPayload`

`actorOrderRouting` 已退役，`input_order_mappings.json` 不再内嵌 actor 侧候选 DSL。共享输入动作（如 `Command`）只声明输入触发、目标来源和 typed `orderPayload`；逐 actor 路由归属 `CommandIntentProfile`。

```json
{
  "actionId": "Command",
  "requireSelection": true,
  "targetType": "HoveredEntityOrPosition",
  "orderTypeKey": "moveTo",
  "orderPayload": {
    "kind": "MoveToWorldCm"
  }
}
```

### 路由规则

- Input mapping 不再声明 `isSkillMapping`；是否是 ability order 由 `orderPayload.kind` 和 order type contract 派生。
- Input mapping 不再声明 `argsTemplate.i0..i3`；`castAbility` 使用 `orderPayload.abilitySlot`。
- 同一动作若需要按 actor/target 事实分流，必须使用 `CommandIntentProfile`；同一 mapping 不能同时拥有两套路由真相。
- `actorOrderRouting`、`isSkillMapping`、`argsTemplate` 出现在 authored JSON 时，loader 必须加载期失败。

### Typed payload 示例

```json
{
  "actionId": "AbilityQ",
  "orderTypeKey": "castAbility",
  "targetType": "Entity",
  "orderPayload": {
    "kind": "CastAbility",
    "abilitySlot": 0
  }
}
```

## 3 Target layout profiles

`groupMoveTargetLayout` 已退役。多 actor position move 的目标展开由独立 `targetLayoutProfiles` 声明，再由 mapping 通过 `targetLayoutProfileId` 引用：

```json
{
  "targetLayoutProfiles": [
    {
      "id": "layout.move.grid",
      "mode": "Grid",
      "spacingCm": 140,
      "orderTypeKeys": [ "moveTo" ]
    }
  ],
  "mappings": [
    {
      "actionId": "Command",
      "orderTypeKey": "moveTo",
      "orderPayload": { "kind": "MoveToWorldCm" },
      "targetLayoutProfileId": "layout.move.grid"
    }
  ]
}
```

- `mode: Grid` 时 `orderTypeKeys` 必填且非空；Core 不得硬编码具体 order type key。
- loader 校验 profile id 唯一、mapping 引用存在、旧 `groupMoveTargetLayout` 明确失败。

## 4 Order：`instantComplete` + `persistentStoredTarget`

Mod 在 `order_types.json` 注册 instant-complete order：

| 字段 | 含义 |
|------|------|
| `instantComplete: true` | 由 `InstantCompleteOrderSystem`（`AbilityActivation` phase）立即完成 |
| `persistentStoredTarget` | 完成时将 order target/spatial 写入 mod 注册的 blackboard keys |
| `spatialBlackboardKey` / `entityBlackboardKey` | 应设为 `none`，避免 order 执行期 blackboard 与持久化 keys 冲突 |

`InstantCompleteOrderSystem` 要求 `instantComplete=true` 的 order 必须完整配置 `persistentStoredTarget`。

## 5 Blackboard：`BlackboardStoredTargetOps`

通用读写/提交 API，key 集合由 Mod 在 `orderBlackboardKeys` 注册：

- `Point` / `HexCell` / `Entity` 三种 target kind
- `CommitFromOrder` 优先级：Entity > Hex > Point

## 6 Effect：`SubmitOrderFromBlackboard`

`presetType: SubmitOrderFromBlackboard`（`lifetime: Instant`）在 on-spawn 等时机从 source entity 读取 stored target，向 target entity 提交 Mod 配置的 typed order：

- point/hex → `pointMoveOrderTypeKey`（如 `moveTo`，order type 必须声明 `payloadKind: MoveToWorldCm`）
- entity → `entityOrderTypeKey` + typed `entityOrderPayload`（如 `{ "kind": "CastAbility", "abilitySlot": 1 }`）
- 目标 entity 必须带 `PlayerOwner`；Core 不接受 `Team.Id` 作为 player 身份 fallback
- source 无 stored target 时不提交 order（静默 no-op，由 Mod 决定是否配置 on-spawn effect）

Train CreateUnit 挂接示例：

```json
{
  "unitCreation": {
    "onSpawnEffect": "Effect.Rts.Shared.ApplySpawnTargetOrder",
    "copySourcePlayerOwner": true
  }
}
```

## 7 Mod 挂靠示例

RtsDemoMod：

- `setSpawnTarget` order + `Rts.SpawnTarget.*` keys
- Command 路由：Train slot → set spawn target；其他单位 → moveTo
- `Effect.Rts.Shared.ApplySpawnTargetOrder` 挂于 train CreateUnit 的 `unitCreation.onSpawnEffect`

## 8 深度材料

- 机制说明：`docs/architecture/interaction/features/companion/r6_rally_point.md`
- Kanban：SY56 Blackboard Rally Point、SY14 Actor Routing
