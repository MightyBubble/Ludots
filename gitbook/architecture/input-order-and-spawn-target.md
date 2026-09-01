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
          "abilityIdKeySuffix": ".Train",
          "blockedAnyTags": [ "Progression.Rts.WarpGate" ]
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
- `abilityIdKey` / `abilityIdKeySuffix`：与有效 ability id 精确或子串匹配
- `blockedAnyTags`：actor 带任一 tag 时不匹配（如 Gateway 研究 WarpGate 后跳过 Train 路由）
- 多 candidate 命中时取最高 `priority`

### 前置条件

- `isSkillMapping: false`：actor 路由仅用于非技能 Command 类输入
- `TagOps` 必须已注册：`LocalOrderSourceHelper` 在 mapping 创建期检测 `actorOrderRouting`，缺少 TagOps 时 fail-fast（不延迟到运行时 Command）
- per-candidate `selectionType` 可覆盖 mapping 级默认值

### Per-candidate selection

- `selectionType` 可选；缺省时继承 mapping 级 `selectionType`
- `HoveredEntityOrPosition`：优先 hovered command target，否则 ground position（用于 point rally 与 entity/garrison rally 共用一条路由）

Resolver 由 `CoreInputMod.LocalOrderSourceHelper` 注入；mapping 声明了 `actorOrderRouting` 但未注册 TagOps 时，在 mapping 创建期 fail-fast。

## 3 Group move target layout

`groupMoveTargetLayout` 为全局配置，控制多 actor position move 的中性网格目标偏移；它不是 Formation capability：

```json
{
  "groupMoveTargetLayout": {
    "mode": "Grid",
    "assignment": "PreserveRelative",
    "spacingCm": 140,
    "orderTypeKeys": [ "moveTo" ]
  }
}
```

- `mode: Grid` 时 `orderTypeKeys` 必填且非空；Core 不得硬编码具体 order type key
- `mode: Grid` 时 `assignment` 必须显式选择 `ActorOrder` 或 `PreserveRelative`，不提供默认回退
- `ActorOrder` 按批次中的 actor 顺序分配槽位，供明确依赖既有顺序的 Mod 使用
- `PreserveRelative` 以目标点相对群体中心的移动方向为前轴，先保持前后顺序，再保持左右顺序；相同投影按原始序号稳定决胜
- 混合 actor 路由提交时，仅对实际 order type 命中 `orderTypeKeys` 且携带单点世界坐标的子集应用布局
- command source 展开为多个成员时，按 source 分配槽位，同一 source 的所有成员共享目标
- `PreserveRelative` 缺 actor 世界坐标或目标点与群体中心重合时，整批返回验证失败；不得回退到 `ActorOrder` 或提交部分订单

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

`presetType: SubmitOrderFromBlackboard`（`lifetime: Instant`）在 on-spawn 等时机从 source entity 读取 stored target，向 target entity 提交 Mod 配置的 order：

- point/hex → `pointMoveOrderTypeKey`（如 `moveTo`）
- entity → `entityOrderTypeKey` + `entityOrderIntArg0`（如 garrison `castAbility` slot 1）
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
