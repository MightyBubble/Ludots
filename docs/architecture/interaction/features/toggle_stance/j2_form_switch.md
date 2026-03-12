# J2: 全技能组切换 (变身)

> 清单编号: J2 | 游戏示例: LoL Elise/Jayce/Nidalee, Dota Troll

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键在两种形态间切换，交互层仅触发执行；形态路由由 form tag 决定。

## Ludots实现

```
Down → Execute:
  1. if HasTag("form_ranged"):
     RemoveTag("form_ranged") + AddTag("form_melee")
  2. else:
     RemoveTag("form_melee") + AddTag("form_ranged")

技能路由:
  ability_slot_0 的 AbilityDefinition 绑定两个 ability:
    form_melee  → melee_Q
    form_ranged → ranged_Q

  AbilityExecSystem 激活时:
    读取 form tag → 选择对应 ability
```

Form tag 的存在决定当前激活的技能组。每个 ability slot 在执行时检查 form tag，路由到对应的实际 ability 定义。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | form tag 切换 |
| AbilityStateBuffer | ⚠️ 需要扩展 | 支持 form-based ability mapping (slot + tag → actual ability) |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Form-based ability mapping (slot + tag → ability) | P1 | AbilityStateBuffer 需支持 form tag 路由，J2/J3/J4 共用 |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
