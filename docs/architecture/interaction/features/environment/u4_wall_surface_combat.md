# U4: Wall Surface Combat

## 机制描述
在墙壁或表面上进行战斗（墙跑、墙跳攻击等）。

## 交互层设计
- **Input**: Down
- **Selection**: None / Direction
- **Resolution**: ContextScored（检测墙壁接触）

## 实现要点
```
ContextGroup:
  candidate: "WallRunAttack"
    precondition: self HasTag("wall_contact") AND velocity.y > 0
    score: 130

Phase Graph:
  1. CheckPrecondition: wall_contact == true
  2. ApplyEffect(wall_run_attack)
  3. ApplyEffect(launch_from_wall)

// 墙壁接触检测:
WallContactSystem:
  OnCollision(entity, wall):
    if wall.HasTag("climbable"):
      AddTag(entity, "wall_contact", duration=30 ticks)
```
- 墙壁 entity 有 Tag("climbable") 或 Layer("Wall")
- 墙跑状态通过 Tag 标识，持续时间有限

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Wall contact detection | P2 | 碰撞检测墙壁接触状态 |

## 参考案例
- **Titanfall**: 墙跑射击
- **Mirror's Edge**: 墙壁跑酷战斗
- **Warframe**: 墙跑攻击
