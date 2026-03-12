# E2: Linear Penetrate Skillshot

> 清单覆盖: E2 直线弹道(穿透)

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

### E2: 直线弹道(穿透)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_penetrating_projectile
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - Speed: 1500 cm/s
      - MaxRange: 5000 cm
      - CollisionRadius: 80 cm
      - BlockedBy: [Terrain]  // 只被地形阻挡
      - OnHit: ApplyEffect(damage_effect)
      - DestroyOnHit: false  // 穿透
      - MaxPierceCount: -1  // 无限穿透
```

**实现差异**:
- `DestroyOnHit = false` → 命中后继续飞行
- `ProjectileRuntimeSystem` 需维护 `_hitEntities` HashSet 避免重复命中同一目标

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| DestroyOnHit flag | ⚠️ 需扩展 | 支持穿透模式 |
| HitEntities tracking | ⚠️ 需扩展 | 避免重复命中 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Penetrate mode | P1 | DestroyOnHit=false + 命中记录 |
| MaxPierceCount | P2 | 限制穿透次数 (如 Sivir Q 穿2次) |
