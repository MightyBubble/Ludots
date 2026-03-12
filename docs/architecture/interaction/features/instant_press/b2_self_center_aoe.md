# B2: 以自身为中心AoE

> 清单编号: B2 | 游戏示例: LoL Amumu R, Dota Ravage, GoW Spartan Rage爆发

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，以自身为中心对周围区域造成影响。

## Ludots实现

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → aoe_search_effect
    → EffectPresetType: Search
    → BuiltinHandler: SpatialQuery (radius=500cm, filter=Enemy)
    → OnHit: FanOutApplyEffect(stun_effect)
```

使用 Search preset 进行空间查询，对范围内所有符合条件的目标施加效果。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| Search preset | ✅ 已有 | SpatialQuery + DispatchPayload |
| HandleSpatialQuery | ✅ 已有 | 空间查询处理器 |
| HandleDispatchPayload | ✅ 已有 | 效果分发处理器 |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
