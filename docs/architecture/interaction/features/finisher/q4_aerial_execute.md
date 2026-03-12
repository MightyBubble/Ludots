# Q4: Aerial Execute

## 机制描述
当自身高度高于目标时（居高临下），可以执行空中处决技能。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: ContextScored
- **Precondition**: `self.height > target.height + threshold`

## 实现要点
```
ContextGroup:
  candidate: "AerialExecute"
    precondition: HeightPrecondition(self.height - target.height > 200cm)
    score: 110

Phase Graph:
  1. CheckHeight(self, target, min_diff=200cm)
  2. ApplyEffect(aerial_slam_damage)
  3. ApplyEffect(knockdown)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Height precondition | P2 | Graph ops 或 builtin 支持高度差判断 |

## 参考案例
- **Assassin's Creed**: 高处跳下暗杀
- **Shadow of Mordor**: 空中处决
- **Batman Arkham**: 高处俯冲攻击
