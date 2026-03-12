# E5: Arc Trajectory Skillshot

> 清单覆盖: E5 弧线弹道(抛物线)

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

### E5: 弧线弹道(抛物线)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_arc_projectile
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - TrajectoryType: Arc
      - InitialVelocity: (horizontal: 800, vertical: 600)
      - Gravity: -980 cm/s²
      - MaxRange: 1500 cm
      - OnHit: ApplyEffect(aoe_damage_effect)
      - DestroyOnHit: true
      - DestroyOnGround: true
```

**物理模拟**:
```
每 tick:
  velocity.y += gravity * dt
  position += velocity * dt
  if (position.y <= groundLevel) → 触发 OnGround
```

**示例**: OW Junkrat 榴弹, GoW 掷斧

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| TrajectoryType | ❌ 需新增 | 支持 Arc 模式 |
| Gravity simulation | ❌ 需新增 | 垂直速度衰减 |
| Ground collision | ⚠️ 需扩展 | 检测落地 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Arc trajectory | P2 | 抛物线物理模拟 |
| Gravity parameter | P2 | 重力加速度配置 |
| OnGround trigger | P2 | 落地触发 AoE |
