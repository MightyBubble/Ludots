# Mechanism: K5 — Portal Pair (放置双端传送门)

> **Examples**: OW Symmetra传送门(入口+出口), Portal游戏

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point** (两次)
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: SelectionGate → 等待第一个点 (入口)
    → OrderRequest: Position
  Item[1]: EffectSignal → create_portal_entrance
    → CreateUnit(portal_entrance, position=point_1)
    → WriteBlackboard(portal_entrance_id, entity_id)

  Item[2]: SelectionGate → 等待第二个点 (出口)
    → OrderRequest: Position
  Item[3]: EffectSignal → create_portal_exit
    → CreateUnit(portal_exit, position=point_2)
    → WriteBlackboard(portal_exit_id, entity_id)

  Item[4]: EffectSignal → link_portals
    → Phase Graph:
      1. ReadBlackboard(portal_entrance_id)
      2. ReadBlackboard(portal_exit_id)
      3. AddTag(entrance, "portal_linked", metadata=exit_id)
      4. AddTag(exit, "portal_linked", metadata=entrance_id)
```

**传送逻辑** (在 portal entity 上):
```
ResponseChainListener:
  eventTagId: on_unit_enter
  precondition: HasTag("portal_linked")
  responseType: Chain
    → ReadTag(metadata=linked_portal_id)
    → Teleport(unit, target_portal_position)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| SelectionGate | ✅ 已有 |
| CreateUnit handler | ✅ 已有 |
| Blackboard write/read | ✅ 已有 |
| Tag metadata | ⚠️ 需扩展 |
| Teleport handler | ✅ 已有 |
| Proximity trigger | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Tag metadata storage | P1 | Tag携带关联实体ID |
| Portal link mechanism | P1 | 双向传送门关联 |
| Teleport cooldown | P2 | 防止无限传送循环 |
| Portal lifetime | P2 | 超时自动销毁 |
