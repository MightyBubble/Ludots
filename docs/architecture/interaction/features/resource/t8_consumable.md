# T8: Consumable

## 机制描述
消耗品（药水、食物等）有数量限制，每次使用消耗一个，用完即无法再使用。

## 交互层设计
- **Gate**: Attribute Precondition（数量门控）
- **不影响交互层**，通过 Attribute + Precondition 实现

## 实现要点
```
AbilityActivationRequire:
  AttributePrecondition:
    self.item_count > 0

Phase Graph:
  1. CheckPrecondition: item_count > 0
  2. ApplyEffect(consumable_effect)  // 治疗/增益等
  3. ModifyAttribute(self, item_count, -1)
  4. if item_count == 0:
       UI: ShowEmptyMessage("药水耗尽")
```
- `item_count` 存储在物品/技能槽的 Attribute 上
- 多种消耗品通过不同 abilityId + 不同 item_count Attribute 区分

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P0 | 复用 G8 需求 |

## 参考案例
- **Dark Souls**: 饮血瓶（有限次数）
- **Diablo**: 药水格子
- **Dota 2**: 补给物品消耗
