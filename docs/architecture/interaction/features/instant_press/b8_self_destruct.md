# B8: 自爆/牺牲

> 清单编号: B8 | 游戏示例: Dota Techies自爆, LoL Kog'Maw被动

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，以自身为中心造成大量伤害，同时自身死亡。

## Ludots实现

```
InputOrderMapping:
  actionId: "SelfDestruct"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → self_destruct_effect
    → Phase Graph:
      1. SpatialQuery(origin=self, radius=500cm, filter=Enemy)
      2. FanOutApplyEffect(massive_damage_template)
      3. ModifySelfAttribute(health, set=0)
```

先对周围敌人造成伤害，然后将自身生命值设为 0。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| Search preset | ✅ 已有 | SpatialQuery + FanOut |
| Attribute system | ✅ 已有 | 修改自身生命值 |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
