# Input Order Routing 与 Spawn Target 基建

本页描述 R6 spawn target / rally 所依赖的正式 Core 基建。业务语义（RTS producer rally、garrison 等）由 Mod 配置，Core 只提供通用原语。

## 1 设计原则

- Core 不得硬编码 rally、producer、或具体 Mod 的 order type key
- 行为 SSOT 在 JSON：`order_types.json`、`input_order_mappings.json`、`effects.json`
- 禁止代码层 fallback：路由 unmatched actor 时跳过，不注入默认 order type

## 2 Input：`actorOrderRouting`

在 `input_order_mappings.json` 中，共享输入动作（如 `Command`）可按选中 actor 路由到不同 `orderTypeKey`：

```json
{
  "actionId": "Command",
  "requireSelection": true,
  "selectionType": "Position",
  "actorOrderRouting": {
    "candidates": [
      {
        "orderTypeKey": "setSpawnTarget",
        "priority": 10,
        "selectionType": "HoveredEntityOrPosition",
        "match": {
          "abilitySlotIndex": 2,
          "abilityIdKeySuffix": ".Train"
        }
      },
      {
        "orderTypeKey": "moveTo",
        "priority": 0,
        "match": {}
      }
    ]
  }
}
```

### 匹配规则（`ActorOrderRoutingMatcher`）

- `requiredAllTags` / `blockedAnyTags`：空列表表示无约束
- `abilitySlotIndex`：通过 `AbilitySlotResolver` 解析 form/grant 层后的有效 slot
- `abilityIdKey` / `abilityIdKeySuffix`：与有效 ability id 精确或后缀匹配
- 多 candidate 命中时取最高 `priority`

### Per-candidate selection

- `selectionType` 可选；缺省时继承 mapping 级 `selectionType`
- `HoveredEntityOrPosition`：优先 hovered command target，否则 ground position（用于 point rally 与 entity/garrison rally 共用一条路由）

Resolver 由 `CoreInputMod.LocalOrderSourceHelper` 注入；未配置 resolver 且 mapping 声明了 `actorOrderRouting` 时运行时 fail-fast。

## 3 Order：`instantComplete` + `persistentStoredTarget`

Mod 在 `order_types.json` 注册 instant-complete order：

| 字段 | 含义 |
|------|------|
| `instantComplete: true` | 由 `InstantCompleteOrderSystem`（`AbilityActivation` phase）立即完成 |
| `persistentStoredTarget` | 完成时将 order target/spatial 写入 mod 注册的 blackboard keys |
| `spatialBlackboardKey` / `entityBlackboardKey` | 应设为 `none`，避免 order 执行期 blackboard 与持久化 keys 冲突 |

`InstantCompleteOrderSystem` 要求 `instantComplete=true` 的 order 必须完整配置 `persistentStoredTarget`。

## 4 Blackboard：`BlackboardStoredTargetOps`

通用读写/提交 API，key 集合由 Mod 在 `orderBlackboardKeys` 注册：

- `Point` / `HexCell` / `Entity` 三种 target kind
- `CommitFromOrder` 优先级：Entity > Hex > Point

## 5 Effect：`SubmitOrderFromBlackboard`

`presetType: SubmitOrderFromBlackboard`（`lifetime: Instant`）在 on-spawn 等时机从 source entity 读取 stored target，向 target entity 提交 Mod 配置的 order：

- point/hex → `pointMoveOrderTypeKey`（如 `moveTo`）
- entity → `entityOrderTypeKey` + `entityOrderIntArg0`（如 garrison `castAbility` slot 1）

## 6 Mod 挂靠示例

RtsDemoMod：

- `setSpawnTarget` order + `Rts.SpawnTarget.*` keys
- Command 路由：Train slot → set spawn target；其他单位 → moveTo
- `Effect.Rts.Shared.ApplySpawnTargetOrder` 挂于 train `onSpawnEffect`

## 7 深度材料

- 机制说明：`docs/architecture/interaction/features/companion/r6_rally_point.md`
- Kanban：SY56 Blackboard Rally Point、SY14 Actor Routing
