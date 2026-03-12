# Q3: Backstab

## 机制描述
从目标背后发动攻击时，可以执行背刺技能造成额外伤害或暴击。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: ContextScored
- **Precondition**: `angle_to_target_back < 30°`

## 实现要点
```
ContextGroup:
  candidate: "Backstab"
    precondition: AnglePrecondition(self, target, max_angle=30°)
    score: 120

Phase Graph:
  1. CheckAngle(self.forward, target.back) < 30°
  2. ApplyEffect(damage * 3.0, crit=true)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Angle precondition | P2 | Graph ops 或 builtin 支持角度判断 |

## 参考案例
- **Dark Souls**: 背后攻击可以触发背刺动画
- **Dishonored**: 背后暗杀一击必杀
- **TF2 Spy**: 背刺即死
