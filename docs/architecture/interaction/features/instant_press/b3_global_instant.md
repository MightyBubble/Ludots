# B3: 全图即时生效

> 清单编号: B3 | 游戏示例: LoL Karthus R, Dota Zeus R/Silencer R

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，对全地图所有符合条件的目标造成影响。

## Ludots实现

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → global_damage_effect
    → Phase Graph:
      1. QueryRadius(origin=caster, radius=999999)  // 全图
      2. QueryFilterRelationship(Hostile)
      3. FanOutApplyEffect(damage_template)
```

使用超大半径的空间查询实现全图效果，或使用 Search preset 配置全图范围。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| Search preset | ✅ 已有 | 支持超大半径查询 |
| Phase Graph | ✅ 已有 | QueryRadius + QueryFilter + FanOut |

## 新增需求

无。可使用现有 Search preset 配置超大半径实现全图效果。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
