# Q6: Ultimate Meter

## 机制描述
当大招仪表充满时，可以释放终极技能。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Precondition**: `self.ultimate_charge >= 100`

## 实现要点
```
AbilityActivationRequire:
  AttributePrecondition:
    self.ultimate_charge >= 100

InputOrderMapping:
  actionId: "UltimateAbility"
  selectionType: None
  interactionMode: Explicit

Phase Graph:
  1. ModifyAttribute(self, ultimate_charge, -100)
  2. ApplyEffect(ultimate_effect)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Attribute precondition | P0 | 复用 G8 需求，支持 Ability 级别的 Attribute 门控 |

## 参考案例
- **Overwatch**: 大招仪表充满后按 Q 释放
- **Valorant**: 终极技能点数充满后释放
- **Apex Legends**: 大招充能完成后释放
