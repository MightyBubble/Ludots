# B4: 闪烁/短距瞬移

> 清单编号: B4 | 游戏示例: LoL Flash, Dota Blink Dagger

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None** (或 **Direction**)
- **Acquisition**: **Explicit**

玩家按键即生效，向面朝方向或指定方向瞬移固定距离。

## Ludots实现

```
InputOrderMapping:
  actionId: "Flash"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → blink_effect
    → Phase Graph:
      1. ReadBlackboardEntity(target_pos)  // 如有, 否则用自身面朝方向
      2. WriteBlackboardFloat(pos_x, new_x)
      3. WriteBlackboardFloat(pos_y, new_y)
    → 或: BuiltinHandler: ApplyForce(instant teleport mode)
```

需要确认 `ApplyForce` handler 是否支持瞬移语义，或需新增专用 `Teleport` handler。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| ApplyForce handler | ✅ 已有 | 力/位移处理器 |
| Phase Graph | ✅ 已有 | Blackboard 读写 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Teleport handler 或 ApplyForce instant 模式 | P1 | 需要确认现有 ApplyForce 是否支持瞬移语义，或新增专用 Teleport handler |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
