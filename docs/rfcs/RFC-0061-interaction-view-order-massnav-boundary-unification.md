# RFC-0061 Interaction → EntityView → Order → MassNav 职责边界收敛

Status: Proposed（Epic #522 SSOT）  
Parent Epic: [#522](https://github.com/MightyBubble/Ludots/issues/522)

## 1. 问题

当前 MassNavigation 与 Input / Selection 职责交叉：

- MassNav 直接读 `AuthoritativeInput`、`SelectionRuntime`，旁路 `OrderQueue` 调 `OrderBufferSystem.SubmitOrder`
- Selection 同时承担 Input 子模块与全栈 gameplay SSOT
- 框选、命令、移动、表现各走不同「选中真相」，Mod 无法数据驱动扩展

本 RFC 收敛为 **单向依赖链**，并为后续 Context Stack（RFC-0062）、Control Plane（RFC-0063）、Provenance Performer（RFC-0064）留出挂点。

## 2. 目标链路

```text
CastProfile / InputCastSpec (geometry 无关)
  → Client raw collection (UiAcquisition)
    → FilterProfile (association / eligibility)
      → (playerRepEntity, activeCollectionKey)   ← Context Stack 决定 key
        → EntityView profile (viewKey → collection + role)
          → OrderQueue（唯一 intake）
            → OrderBuffer
              → MassNavigation ingestion（纯 execution）
```

## 3. 三条铁律

1. **OrderQueue 是唯一 order intake** — MassNav / evidence / AI / input 不得旁路直写 `SubmitOrder`
2. **MassNav 不读 Input / Selection** — 只消费 OrderBuffer 中已提交的 movement order
3. **Selection 概念退役** — 「命令目标集」由 `EntityCollectionStore` + `EntityCollectionRoleKind.CommandSource`（及 context-bound key）表达；`SelectionRuntime` 不得再作为 hub

## 4. EntityView 与 Collection

### 4.1 地址模型

Collection 真相地址：`(owner entity, collection key string)`。

- **owner** = participant representative entity（player rep），不是 `PlayerId` 整数，不是全局 service bag
- **key** = 由 Interaction Context Stack 决定的 active key（见 RFC-0062）

### 4.2 EntityView Profile（data）

```json
{
  "viewKey": "command.default",
  "collectionKey": "collection.command.source",
  "role": "CommandSource"
}
```

EntityView 是 **只读绑定**：`viewKey → (collectionKey, role)`。不复制 row，不创造第二套选中真相。

### 4.3 与 RFC-0059 的关系

RFC-0059 的 selection container / member relation **不得**继续作为命令 intake SSOT。允许：

- 短期：acquisition 仍写 `collection.ui.selection.acquisition`，经 filter 写入 command collection
- 终态：删除 `SelectionRuntime` 作为命令 hub 的所有 consumer；formation / snapshot 若仍需容器语义，迁移为 keyed collection + lease descriptor

## 5. CastProfile 与 InputCast

CastProfile 描述 **client input cast 的几何与空间**，与 commit 语义正交：

| 轴 | 归属 | 示例 |
|----|------|------|
| Geometry | InputCastSpec | box / polygon / ray / lasso |
| Space | InputCastSpec | screen / world / minimap |
| Filter | FilterProfile | controllable_by(localPlayerRep) |
| Commit | Order mapping | press / release / smartcast |
| Active collection key | Context Stack | default vs ability.nuke.targets |

禁止把「框选语义」硬编码进 `InteractionModeType` 单一 enum。

## 6. 分层边界

| 层 | 职责 | 禁止 |
|----|------|------|
| Input Cast | 几何 → raw hits collection | 写 command collection、改 association |
| Context Stack | active key / push-pop | 存 selection 真相 |
| Filter Profile | raw → filtered | 改 relationship graph |
| Collection Write | filtered → `(playerRep, key)` | merge 跨 player 的 namespace |
| Order Intake | 读 command collection + fan-out | 读 SelectionRuntime hub |
| MassNav | OrderBuffer execution | 读 Input / Selection |

## 7. Sub-issues（ORD-*）

Parent: [#522](https://github.com/MightyBubble/Ludots/issues/522)

| ID | Issue | 要点 |
|----|-------|------|
| ORD-1 | [#567](https://github.com/MightyBubble/Ludots/issues/567) | MassNav 停止读取 Input / Selection |
| ORD-2 | [#568](https://github.com/MightyBubble/Ludots/issues/568) | OrderQueue 统一 intake 护栏 |
| ORD-3 | [#569](https://github.com/MightyBubble/Ludots/issues/569) | EntityView profile registry |
| ORD-4 | [#570](https://github.com/MightyBubble/Ludots/issues/570) | CommandSource collection → Order fan-out |
| ORD-5 | [#571](https://github.com/MightyBubble/Ludots/issues/571) | Acquisition → Filter → CommandSource 管线 |
| ORD-6 | [#572](https://github.com/MightyBubble/Ludots/issues/572) | Retire SelectionRuntime command consumers |
| ORD-7 | [#573](https://github.com/MightyBubble/Ludots/issues/573) | `#499` publisher 改 relationship query |
| ORD-8 | [#574](https://github.com/MightyBubble/Ludots/issues/574) | 文档回写 + playable showcase |

<details><summary>原表格（策划摘要）</summary>

| ID | 标题 | 要点 |
|----|------|------|
| ORD-1 | MassNav 停止读取 Input / Selection | 删 direct input/selection 依赖 |
| ORD-2 | OrderQueue 统一 intake 护栏 | ArchitectureTests 禁止旁路 SubmitOrder |
| ORD-3 | EntityView profile registry | viewKey → collection + role |
| ORD-4 | CommandSource collection → Order fan-out | 替代 `InputOrderMappingSystem` 读 Selection |
| ORD-5 | Acquisition → Filter → CommandSource 管线 | 对接 RFC-0062 filter |
| ORD-6 | Retire SelectionRuntime command consumers | champion sandbox / MassNav / panel |
| ORD-7 | `#499` publisher 改 relationship query | 见 RFC-0063 |
| ORD-8 | 文档回写 + playable showcase | entity_query_tactics 或新 capability mod |

</details>

## 8. 依赖

- 前置：EntityCollectionStore、OrderQueue（已有）
- 并行：[#239 AAC](https://github.com/MightyBubble/Ludots/issues/239)（association 基座）
- 后续：RFC-0062 Context Stack、RFC-0063 Control Plane、RFC-0064 Provenance

## 9. 非目标

- 不重写 MassNavigationFlow solver
- 不在本 Epic 实现完整 AbilityAim 退役（见 RFC-0062 interaction phase）
- 不做向后兼容 shim

## 10. 验收

- [ ] MassNav / movement 路径零 `SelectionRuntime` / `AuthoritativeInput` 读取
- [ ] 所有 move/cast order 经 OrderQueue
- [ ] Command 目标集来自 `(playerRepEntity, collection.command.source)` 或 context active key
- [ ] ArchitectureTests 覆盖旁路禁止
- [ ] Playable showcase 演示 box → filter → move order → MassNav
