# T7: Combo Meter Maintain

## 机制描述
连击条记录连续命中次数，被击中时重置，连击条数值作为技能强化条件。

## 交互层设计
- **Gate**: Attribute Precondition（连击条门控）
- **不影响交互层**，通过 Attribute 维护实现

## 实现要点
```
// 连击积累:
OnDealDamage:
  ModifyAttribute(self, combo_meter, +1)

// 被击中时重置:
OnReceiveDamage:
  ModifyAttribute(self, combo_meter, 0)  // Reset to 0

// 连击加成（通过 AttributeDerivedGraph）:
DerivedGraph "combo_damage_bonus":
  LoadSelfAttribute(combo_meter)
  Multiply(0.02)  // 每层 2% 伤害加成
  WriteSelfAttribute(combo_damage_multiplier)

// 连击技能门控:
AbilityActivationRequire:
  AttributePrecondition:
    self.combo_meter >= threshold
```
- 连击条无上限或有上限（设计决定）
- UI: 显示连击数和当前加成百分比

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| OnReceiveDamage reset | P1 | 受伤时触发 combo_meter 归零 |
| AttributeDerivedGraph for combo bonus | P2 | 连击数驱动的伤害加成派生 |

## 参考案例
- **Devil May Cry**: Style Meter 连击评级
- **Batman Arkham**: 连击系统
- **Sifu**: 连击维持得分
