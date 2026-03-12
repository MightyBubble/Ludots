# E9: Wall Reflect Skillshot

> 清单覆盖: E9 反射弹道(碰墙反弹)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

### E9: 反射弹道(碰墙反弹)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_reflecting_projectile
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - TrajectoryType: Linear
      - Speed: 1000 cm/s
      - MaxRange: 3000 cm (累计飞行距离)
      - BlockedBy: [Enemy]  // 只被单位阻挡, 不被地形阻挡
      - ReflectFromTerrain: true
      - MaxReflections: 3
      - OnHit: ApplyEffect(damage_effect)
      - DestroyOnHit: true  // 命中单位后销毁
```

**反射计算**:
```
OnTerrainCollision(hitNormal):
  1. 计算反射方向: reflect = direction - 2 * Dot(direction, normal) * normal
  2. 设置新 velocity = reflect.normalized * speed
  3. reflectionCount++
  4. if (reflectionCount >= MaxReflections) → 销毁
  5. 继续飞行
```

**地形法线获取**:
- Physics2D raycast 获取碰撞法线
- 或预先标记地形段的法线方向

**示例**: Dota 特定地形交互弹射

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| Terrain collision | ⚠️ 需扩展 | 获取碰撞法线 |
| Reflection logic | ❌ 需新增 | 入射角=反射角 |
| MaxReflections | ❌ 需新增 | 反弹次数限制 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ReflectFromTerrain | P3 | 地形法线获取+反射 |
| MaxReflections | P3 | 反弹次数限制 |
| 累计 TraveledCm | P3 | 跨越反弹累计距离 |
