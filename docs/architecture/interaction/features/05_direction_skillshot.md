# Feature: Direction / Skillshot Abilities (E1–E10)

> 清单覆盖: E1 直线可阻挡, E2 直线穿透, E3 锥形, E4 矩形, E5 弧线, E6 回旋, E7 弹射链式, E8 矢量, E9 反射, E10 分裂

## 交互层

| 子类 | TargetMode | 说明 |
|------|-----------|------|
| E1-E7, E9-E10 | **Direction** | 施法者→光标方向 |
| E8 | **Vector** | 两点(起点+终点) |

- **InputConfig**: ReactsTo = **Down**
- **Acquisition**: **Explicit**

## Ludots 映射

### Direction (E1-E7):
```
InputOrderMapping:
  selectionType: Direction
  // InputOrderMappingSystem 计算 caster→cursor 方向写入 OrderArgs.Spatial
```

### Vector (E8):
```
InputOrderMapping:
  selectionType: Vector
  // VectorAimPhase: Origin → Direction
  // Two clicks: origin stored in Spatial[0], endpoint in Spatial[1]
```

已有: `OrderSelectionType.Direction`, `OrderSelectionType.Vector`, vector aiming state machine

## 实现方案

### E1: 直线弹道(可被阻挡)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → launch_projectile
    → EffectPresetType: LaunchProjectile
    → BuiltinHandler: CreateProjectile
    → ProjectileConfig:
      speed: 1200cm/s
      maxRange: 800cm
      hitMode: FirstEnemy     // 命中第一个敌人停止
      onHit: ApplyEffectTemplate(arrow_damage)
```

- 已有: `CreateProjectile` handler, `ProjectileRuntimeSystem`

### E2: 直线穿透

```
同 E1, 但:
  hitMode: Penetrate    // 穿透所有敌人
  onHit: 对每个穿透目标 ApplyEffectTemplate
```

- **需要确认**: `ProjectileRuntimeSystem` 是否支持穿透模式 (hitMode)

### E3: 锥形范围

```
AbilityExecSpec:
  Item[0]: EffectSignal → cone_damage
    → Phase Graph:
      1. QueryCone(origin=caster, direction=order_dir, angle=60°, range=400cm)
      2. QueryFilterRelationship(Hostile)
      3. FanOutApplyEffect(cone_damage_template)
```

- 已有: `QueryCone` Graph op (op 110)

### E4: 矩形/线形

```
Phase Graph:
  1. QueryRectangle(origin, direction, width=100cm, length=600cm)
  2. FanOutApplyEffect(line_damage)
```

- 已有: `QueryRectangle` Graph op (op 111), `QueryLine` (op 112)

### E5: 弧线弹道

```
同 E1, 但 ProjectileConfig 增加:
  trajectoryType: Arc
  arcHeight: 200cm
```

- **需要**: ProjectileRuntimeSystem 支持 Arc 轨迹 (抛物线计算)

### E6: 回旋弹道

```
实现:
  1. Phase 1: LaunchProjectile(direction, speed, maxRange)
     → hitMode: Penetrate
     → onMaxRange: 不销毁, 改为 ReverseDirection
  2. Phase 2: Projectile 返回 caster
     → 返回途中继续 onHit
     → 到达 caster 时销毁
```

- **需要**: Projectile `onMaxRange` 回调 + ReverseDirection 行为

### E7: 弹射/链式

```
实现:
  1. 第一次命中: onHit → ApplyEffectTemplate(bounce_search)
  2. bounce_search Phase Graph:
     a. QueryRadius(hit_position, bounce_radius)
     b. QueryFilterNotEntity(already_hit_entity)
     c. AggMinByDistance → next_target
     d. 如有 next_target → LaunchProjectile(toward next_target)
     e. Decrement bounce_count attribute
     f. if bounce_count > 0 → 重复
```

- 已有: `AggMinByDistance` (op 130), `QueryFilterNotEntity` (op 113)
- **需要**: Projectile 支持 unit-seeking (朝特定 entity 飞行)

### E8: 矢量目标

```
InputOrderMapping:
  selectionType: Vector

AbilityExecSpec:
  Item[0]: EffectSignal → vector_damage
    → Phase Graph:
      1. ReadBlackboardFloat(spatial_x0, y0)  // origin
      2. ReadBlackboardFloat(spatial_x1, y1)  // endpoint
      3. QueryLine(origin, endpoint, width=80cm)
      4. FanOutApplyEffect(line_damage)
```

- 已有: Vector aiming + OrderArgs.Spatial.List

### E9: 反射弹道

- 类似 E1, projectile 碰到 wall entity 后改变方向
- **需要**: Projectile 碰撞墙体检测 + 反射角计算

### E10: 贯穿后分裂

- E2 穿透 + 到达终点时 spawn 多个扇形子弹道
- **需要**: Projectile onMaxRange → spawn N 个子 projectile

## 依赖组件

| 组件 | 状态 |
|------|------|
| Direction selection | ✅ 已有 |
| Vector aiming | ✅ 已有 |
| LaunchProjectile preset | ✅ 已有 |
| ProjectileRuntimeSystem | ✅ 已有 |
| QueryCone/Rectangle/Line | ✅ 已有 |
| AggMinByDistance | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| Projectile hitMode: Penetrate | P1 | E2 |
| Projectile arc trajectory | P2 | E5 |
| Projectile reverse/boomerang | P1 | E6 |
| Projectile unit-seeking (homing) | P2 | E7 |
| Projectile wall-reflect | P3 | E9 |
| Projectile onMaxRange split/spawn | P3 | E10 |
