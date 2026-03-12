# E10: Split Spread Skillshot

> 清单覆盖: E10 贯穿后分裂/扩散

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

### E10: 贯穿后分裂/扩散

**变体 A: 主弹分裂**
```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_splitting_projectile
    → ProjectileDescriptor:
      - TrajectoryType: Linear
      - Speed: 1200 cm/s
      - DestroyOnHit: false  // 穿透
      - OnHit:
          Phase Graph:
            1. SpawnSplitProjectiles(count=2, spreadAngle=30)
```

**变体 B: 贯穿后扩散 (LoL Lux R)**
```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → lux_laser_effect
    → EffectPresetType: Search
    → BuiltinHandler: SpatialQuery
      - ShapeType: Rectangle
      - Length: 3000 cm
      - Width: 200 cm
      - Direction: from input
      - Filter: Enemy
    → OnHit: FanOutApplyEffect(damage_effect)
    // 穿透全部, 无需弹道实体
```

**变体 C: 弹射扩散 (MF 弹射)**
```
OnHit:
  1. 直接伤害命中目标
  2. SpatialQuery(radius=BounceRange, filter=Enemy, excludeHit)
  3. FanOutApplyEffect → 扩散伤害 (衰减)
```

**分裂子弹实现**:
```
SpawnSplitProjectiles(count, spreadAngle):
  baseDir = current velocity direction
  for i in 0..count:
    angle = -spreadAngle/2 + i * (spreadAngle / (count-1))
    dir = Rotate(baseDir, angle)
    CreateUnit(projectile_prefab, position=current, velocity=dir*speed)
```

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| CreateUnit handler | ✅ 已有 | 生成子弹 |
| SpatialQuery handler | ✅ 已有 | 即时搜索 |
| SpawnSplit op | ❌ 需新增 | Phase Graph 分裂操作 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| SpawnSplitProjectiles op | P2 | Graph op: 命中时生成多枚子弹 |
| SpreadAngle | P2 | 扩散角度配置 |
| 即时扫射模式 | P1 | Lux R 用 Rectangle search 代替弹道 |
