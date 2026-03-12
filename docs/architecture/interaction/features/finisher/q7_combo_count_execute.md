# Q7: Combo Count Execute

## 机制描述
当连击数达到特定阈值时，可以执行连击终结技。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Precondition**: `self.combo_meter >= threshold`

## 实现要点
```
AbilityActivationRequire:
  AttributePrecondition:
    self.combo_meter >= 5

InputOrderMapping:
  actionId: "ComboFinisher"
  selectionType: None
  interactionMode: Explicit

Phase Graph:
  1. ApplyEffect(combo_finisher_damage * combo_meter)
  2. ModifyAttribute(self, combo_meter, 0)  // Reset
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Attribute precondition | P0 | 复用 G8 需求，支持 Ability 级别的 Attribute 门控 |

## 参考案例
- **Devil May Cry**: 连击数越高，终结技伤害越高
- **God of War**: 连击条满后可以释放终结技
- **Bayonetta**: 连击终结技
