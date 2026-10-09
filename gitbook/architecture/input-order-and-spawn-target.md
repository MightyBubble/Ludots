# Input Order Routing 与 Spawn Target 基建

本页描述 R6 spawn target / rally 所依赖的正式 Core 基建。业务语义（RTS producer rally、garrison 等）由 Mod 配置，Core 只提供通用原语。

## 1 设计原则

- Core 不得硬编码 rally、producer、或具体 Mod 的 order type key
- 行为 SSOT 在 JSON：`order_types.json`、`Input/command_intent_profiles.json`、`effects.json`
- 禁止代码层 fallback：没有规则命中的 actor 直接跳过，不注入默认 order type

## 2 Input：按 actor 路由的下令意图

共享的“下令”意图（右键 / Command）在 `Input/command_intent_profiles.json` 里按 actor 和目标分流到不同 `orderTypeKey`。交互状态把按键接到意图，提交图调 `SubmitCommandIntent`，`CommandIntentBufferDrainSystem` 按当前交互状态找到意图，再用 profile 给每个成员选路线：

```json
{
  "id": "intent.command.default",
  "groupPolicy": { "kind": "independent" },
  "rules": [
    {
      "priority": 40,
      "actor": {
        "hasAbilityWithCategory": "ability.family.train",
        "noneTags": [ "Progression.Rts.WarpGate" ]
      },
      "target": { "hasEntity": true },
      "route": { "orderTypeKey": "setSpawnTarget", "targetShape": "Entity" }
    },
    {
      "priority": 10,
      "target": { "hasEntity": false },
      "route": { "orderTypeKey": "moveTo", "targetShape": "WorldPositionCm" }
    }
  ]
}
```

### 匹配规则

- `actor.hasAbilityWithCategory`：actor 有效 slot（form/grant 叠加后）里有该类别的技能
- `actor.allTags` / `anyTags` / `noneTags`：空表示不约束；`noneTags` 用来在 Gateway 研究 WarpGate 后跳过训练路由
- `target.hasEntity`：`true` 只配实体目标，`false` 只配地面点，不写就两种都配
- `priority` 在 profile 内唯一，多条命中取最高
- `route.targetShape` 决定提交单点坐标还是实体；`exactGroundPoint: true` 取指针落地点本身

profile 装载时把字符串编译成 id 和位集；重复 priority、未知 order type、未知关系类型、未知 context group 都在装载期报错。

## 3 多选移动

多个 actor 同时下移动令时，目标点分配写在提交图的 `SubmitCommandIntent` 节点上：

```json
{ "id": "submit", "op": "SubmitCommandIntent", "layout": "preserveRelative", "layoutSpacingCm": 140 }
```

- 不写 `layout`：所有 actor 拿同一个落地点
- `actorOrder`：按成员顺序排网格槽位
- `preserveRelative`：以移动方向为前轴，保持原来的前后、左右顺序
- 写了 `layout` 必须写正数 `layoutSpacingCm`；没写 `layout` 却写了间距，图编译报错

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
- `Input/command_intent_profiles.json`：训练类单位 → `setSpawnTarget`；其他单位 → `moveTo`
- `Effect.Rts.Shared.ApplySpawnTargetOrder` 挂于 train CreateUnit 的 `unitCreation.onSpawnEffect`

## 8 深度材料

- 机制说明：`docs/architecture/interaction/features/companion/r6_rally_point.md`
- Kanban：SY56 Blackboard Rally Point
