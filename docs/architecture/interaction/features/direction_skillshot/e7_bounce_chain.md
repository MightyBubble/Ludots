# E7: Bounce Chain Skillshot

> 清单覆盖: E7 弹射/链式(命中后弹跳)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction** (初始方向) 或 **UnitTarget** (初始目标)
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_or_ground`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

### E7: 弹射/链式

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_bounce_projectile
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - TrajectoryType: Homing  // 追踪目标
      - Speed: 1500 cm/s
      - OnHit: BounceToNext
      - MaxBounces: 5
      - BounceRange: 600 cm
      - BounceFilter: Enemy, NotHitYet
```

**弹跳逻辑**:
```
OnHit(target):
  1. ApplyEffect(damage_effect, target)
  2. 记录 target 到 _hitEntities
  3. 搜索 BounceRange 内最近的 Enemy (排除 _hitEntities)
  4. 如果找到 → 设置新 target, bounceCount++
  5. 如果 bounceCount >= MaxBounces → 销毁
```

**示例**: Dota Shuriken 弹射, LoL Brand R

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| Homing trajectory | ⚠️ 需扩展 | 追踪目标 |
| Bounce logic | ❌ 需新增 | 命中后搜索下一目标 |
| HitEntities tracking | ⚠️ 需扩展 | 避免重复弹跳 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Bounce mechanism | P2 | 命中后搜索+重定向 |
| MaxBounces | P2 | 弹跳次数限制 |
| BounceRange | P2 | 弹跳搜索半径 |
| Homing trajectory | P2 | 自动追踪目标 |
