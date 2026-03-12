# U2: Wall Slam / Ledge Push

## 机制描述
将敌人击飞撞墙造成额外伤害，或推下悬崖造成坠落伤害/即死。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: Explicit / ContextScored

## 实现要点
```
Phase Graph:
  1. ApplyEffect(displacement, direction=away_from_caster, distance=500cm)
  2. Displacement.OnCollision:
       if hit.HasTag("wall"):
         ApplyEffect(target, stun, duration=60 ticks)
         ApplyEffect(target, bonus_damage=200)
       if hit.HasTag("ledge"):
         ApplyEffect(target, fall_damage=500)
         // 或: instant_kill if fall_height > threshold

// 墙壁/悬崖检测:
DisplacementCollisionSystem:
  OnDisplacementCollision(entity, obstacle):
    if obstacle.HasTag("wall"):
      → trigger wall_slam_effect
    if obstacle.HasTag("ledge"):
      → trigger fall_effect
```
- 需要: Displacement 碰撞回调机制
- 墙壁/悬崖通过 Tag 标识

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Displacement 碰撞回调 | P2 | 位移效果碰撞墙壁/悬崖时触发额外效果 |

## 参考案例
- **God of War**: 撞墙额外伤害
- **Batman Arkham**: 环境处决
- **Sekiro**: 推下悬崖即死
