# RFC-0063 Participant Control Plane — Association 归属/代理控制与 Player Entity Collection 域

Status: Proposed  
Epic: [#537](https://github.com/MightyBubble/Ludots/issues/537)

## 1. 问题

当前实现把 **归属** 写成 embodied entity 上的组件：

- `PlayerOwner { PlayerId }`、`Team { Id }` 散落在每个 unit 上
- `ResolvePlayerMembers` / `#499` publisher 扫描 `PlayerOwner` 组件
- 掉线队友单位接管需要「迁移 selection / 重写 PlayerOwner」等 ad-hoc 系统
- Collection 与 `PlayerId` 数据 bag 混谈，重连无法归还框选状态

**归属是游戏场景语义，不是 unit 上的 identity 组件。** 真相应是 **entity 之间的 relationship edge**（AAC #248 `OwnershipResolver` 方向）。

## 2. 结论

### 2.1 实体分类

| 类型 | 组件 | 职责 |
|------|------|------|
| Player rep entity | `PlayerIdentity` | client 在游戏 sim 中的化身 anchor |
| Team rep entity | `TeamIdentity` | 阵营 anchor |
| Embodied entity | GAS / 空间 / 标签 | **无** `PlayerOwner` / `Team` / `PlayerIdentity` |

### 2.2 关系边（catalog SSOT）

| TypeKey | 语义 | 示例 |
|---------|------|------|
| `owns` | 归属 / 所有权 | playerRep ──owns──► marine |
| `controls` | 当前可指挥 | playerRep ──controls──► marine（含代理） |
| `member_of` | 阵营成员 | playerRep ──member_of──► teamRep |
| `ally` | 玩家间同盟 | playerRep1 ──ally──► playerRep2 |

`controls` 是 `owns` 的超集扩展边：正常时 `controls ≡ owns`；代理时仅增 `controls` 边，**不迁移 collection**。

### 2.3 Collection 域

每个 player rep entity 拥有 **完整 collection namespace**：

```text
(player1Rep, collection.command.source) = [m07, m12]
(player2Rep, collection.command.source) = [m99]   // 队友掉线期间仍保留在其 entity 域
```

本地 client 代理指挥 m99 时：

- **Order intake** 通过 filter 读 `controls(player1Rep, ?)` 包含 m99
- **Collection 写入** 默认仍写 `(player1Rep, activeKey)` 供本地操作
- **player2Rep 上的 collection 不搬家** — 重连后 client bind player2Rep 即归还

禁止 `PlayerId → SelectionMap` 全局表。

## 3. 代理控制：Profile + Runtime

### 3.1 静态 Profile（authoring）

```json
{
  "id": "profile.control.offline_teammate_proxy",
  "when": {
    "all": [
      { "relationship": "ally", "between": ["localPlayerRep", "targetPlayerRep"] },
      { "tag": "participant.offline", "on": "targetPlayerRep" }
    ]
  },
  "then": {
    "grantControls": {
      "fromOwner": "targetPlayerRep",
      "toController": "localPlayerRep",
      "edgeType": "controls",
      "scope": "all_owned"
    }
  },
  "revokeWhen": {
    "not": { "tag": "participant.offline", "on": "targetPlayerRep" }
  }
}
```

### 3.2 Runtime

- `AssociationControlProfileRuntime` 评估 profile → `OwnershipResolver` / `RelationshipRuntime` 增删 `controls` 边
- **不** copy collection、**不** 改 `owns` 边、**不** 写 unit 组件

### 3.3 重连归还

```text
T0: player2 offline → controls(p1, m99) granted
T1: player1 框选 [m07, m99] → 写 (p1Rep, command.source)
    (p2Rep, command.source)=[m99] frozen
T2: player2 online → revoke controls(p1, m99)
T3: player2 client bind p2Rep → 读 (p2Rep, command.source)=[m99]
```

无需「归还系统」— 状态从未离开 p2Rep entity 域。

## 4. 控制平面 Query API

拟新增 / 收口：

```csharp
public sealed class ControlDomainQuery
{
    int CollectControlled(Entity controllerRep, Span<Entity> buffer, ControlQueryFlags flags);
    bool IsControllableBy(Entity controllerRep, Entity target);
    bool TryResolveControlDomain(Entity target, out Entity domainRep, out ControlRelationKind kind);
}
```

`CollectControlled` 替代 `ResolvePlayerMembers` 的 `PlayerOwner` 扫描。

## 5. 与 map-owned-participant-contract 的迁移

当前 contract 要求 rep 写 `PlayerOwner` + `Team`，unit 模板带 `PlayerOwner`。

**迁移目标**：

1. Map load 建立 `owns(playerRep, unit)`、`member_of(playerRep, teamRep)` 边
2. 删除 embodied entity 上 `PlayerOwner` / `Team` 组件 authoring
3. `ParticipantBindingResolver` 不再向 rep 以外实体写 owner 组件
4. `#499` `MatchesOwner` 改 `ControlDomainQuery`

## 6. ParticipantView 退役路径

`ParticipantViewCapabilityMod` 写 `SelectionRuntime.LivePrimary` — **错误方向**。

改为：

- Member projection = `ControlDomainQuery` + catalog profile id（非 UX Mode enum）
- Observer focus = 可选 QA entity，不是 `Players | Teams` Mode

## 7. Sub-issues（CTRL-*）

Parent: [#537](https://github.com/MightyBubble/Ludots/issues/537)

| ID | Issue |
|----|-------|
| CTRL-1 | [#554](https://github.com/MightyBubble/Ludots/issues/554) ControlDomainQuery API |
| CTRL-2 | [#556](https://github.com/MightyBubble/Ludots/issues/556) Map load owns/member_of |
| CTRL-3 | [#558](https://github.com/MightyBubble/Ludots/issues/558) 删除 embodied PlayerOwner/Team |
| CTRL-4 | [#560](https://github.com/MightyBubble/Ludots/issues/560) AssociationControlProfile |
| CTRL-5 | [#562](https://github.com/MightyBubble/Ludots/issues/562) #499 publisher relationship 化 |
| CTRL-6 | [#545](https://github.com/MightyBubble/Ludots/issues/545) collection namespace 护栏 |
| CTRL-7 | [#547](https://github.com/MightyBubble/Ludots/issues/547) ParticipantView 停止写 Selection |
| CTRL-8 | [#549](https://github.com/MightyBubble/Ludots/issues/549) Showcase offline proxy |
| CTRL-9 | [#551](https://github.com/MightyBubble/Ludots/issues/551) ArchitectureTests |
| CTRL-10 | [#553](https://github.com/MightyBubble/Ludots/issues/553) gitbook 回写 |

## 8. 依赖

- [#239 AAC](https://github.com/MightyBubble/Ludots/issues/239) / #248 OwnershipResolver（已有）
- RFC-0062 FilterProfile（controllable 过滤）
- RFC-0061 Order intake

## 9. 非目标

- 不重做 RelationshipRuntime 存储
- 不引入 PlayerId 全局 selection service
- 不做 PlayerOwner 兼容读取 fallback

## 10. 验收

- [ ] embodied entity 零 `PlayerOwner` / `Team`（showcase 迁移完成）
- [ ] 控制集合来自 `controls` 边 query
- [ ] offline proxy 仅增删 `controls` 边，collection 不迁移
- [ ] 重连 showcase 证明 p2Rep collection 归还
- [ ] ArchitectureTests 禁止新 PlayerOwner on units
