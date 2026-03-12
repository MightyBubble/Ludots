# B7: 解除控制(净化)

> 清单编号: B7 | 游戏示例: LoL水银QSS, Dota Lotus Orb, DS翻滚解控

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，移除自身所有控制效果和负面状态。

## Ludots实现

```
InputOrderMapping:
  actionId: "Cleanse"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

AbilityExecSpec:
  Item[0]: EffectSignal → cleanse_effect
    → Phase Graph:
      1. 遍历 target 的 GameplayTagContainer
      2. 移除所有带 "cc" 前缀的 tag
      3. 或: 移除所有 EffectTemplate 带 "debuff" tag 的 active effects
```

需要批量移除匹配特定模式的标签或效果。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| GameplayTagContainer | ✅ 已有 | 标签管理 |
| Phase Graph | ✅ 已有 | 效果处理流程 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 批量 Tag/Effect 清除 Graph op | P2 | 需要 Graph op 来批量移除匹配某 pattern 的 tag/effect |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
