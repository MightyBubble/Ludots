# E10: Split Spread Skillshot

> 清单覆盖: E10 贯穿后分裂/扩散

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

### E10: 贯穿后分裂/扩散

**变体 A: 主弹分裂**

> ⚠️ Architecture note: Graph VM cannot perform structural changes (creating/deleting entities, mounting components). Projectile spawning must go through a BuiltinHandler → RuntimeEntitySpawnQueue, not as a Phase Graph op.

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_splitting_projectile
    → ProjectileDescriptor:
      - TrajectoryType: Linear
      - Speed: 1200 cm/s
      - DestroyOnHit: false  // 穿透
      - OnHit:
          → BuiltinHandler: SpawnSplitProjectiles
            - count: 2
            - spreadAngle: 30
            // Handler runs outside Graph Phase, queues spawns via RuntimeEntitySpawnQueue
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

**分裂子弹实现** (BuiltinHandler, runs outside Graph Phase):
```
BuiltinHandler: SpawnSplitProjectiles(count, spreadAngle):
  baseDir = current velocity direction
  for i in 0..count:
    angle = -spreadAngle/2 + i * (spreadAngle / (count-1))
    dir = Rotate(baseDir, angle)
    RuntimeEntitySpawnQueue.Enqueue(projectile_prefab, position=current, velocity=dir*speed)
  // Entities are created after Graph Phase completes
```

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| CreateUnit handler | ✅ 已有 | 生成子弹 |
| SpatialQuery handler | ✅ 已有 | 即时搜索 |
| SpawnSplitProjectiles handler | ❌ 需新增 | BuiltinHandler: 命中时通过 RuntimeEntitySpawnQueue 生成子弹 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| SpawnSplitProjectiles handler | P2 | BuiltinHandler: 命中时通过 RuntimeEntitySpawnQueue 生成多枚子弹 |
| SpreadAngle | P2 | 扩散角度配置 |
| 即时扫射模式 | P1 | Lux R 用 Rectangle search 代替弹道 |
