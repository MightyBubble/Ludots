# E1: Linear Blockable Skillshot

> 清单覆盖: E1 直线弹道(可被阻挡)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

### E1: 直线弹道(可被阻挡)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_projectile
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - Speed: 1000 cm/s
      - MaxRange: 2000 cm
      - CollisionRadius: 50 cm
      - BlockedBy: [Enemy, Terrain]
      - OnHit: ApplyEffect(stun_effect)
      - DestroyOnHit: true
```

**ProjectileRuntimeSystem** 已有:
- 每 tick 推进位置
- 碰撞检测 (Physics2D overlap)
- 命中第一个目标后销毁

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| `graph.aim.rule.ground_only` | AimInteractionMod | 地面或方向点 |
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进+碰撞 |
| ProjectileState component | ✅ 已有 | Speed, TraveledCm, MaxRange |
| Physics2D overlap | ✅ 已有 | 碰撞检测 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| BlockedBy filter | P1 | 需支持 Terrain 阻挡 |
| DestroyOnHit flag | P1 | 命中后销毁逻辑 |
