# J6: 弹药/元素类型切换

> 清单编号: J6 | 游戏示例: GoW Atreus箭矢(光/暗)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键循环切换弹药/元素类型，技能执行时读取当前类型选择对应 EffectTemplate。

## Ludots实现

```
Down → cycle ammo_type Attribute:
  current = ReadAttribute(ammo_type)
  next = (current + 1) % max_types
  WriteAttribute(ammo_type, next)

技能执行时读 ammo_type → 选不同 EffectTemplate:
  if ammo_type == 0: fire_arrow_effect
  if ammo_type == 1: ice_arrow_effect
  if ammo_type == 2: lightning_arrow_effect
```

弹药类型存储在 Attribute 中，切换技能仅修改 Attribute 值。实际射击技能在执行时读取 Attribute，通过 Phase Graph 条件分支选择对应的 EffectTemplate。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| Attribute system | ✅ 已有 | ammo_type Attribute 读写 |
| Phase Graph | ✅ 已有 | 条件分支选择 EffectTemplate |
| AbilityExecSpec | ✅ 已有 | 技能执行时读 Attribute |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
