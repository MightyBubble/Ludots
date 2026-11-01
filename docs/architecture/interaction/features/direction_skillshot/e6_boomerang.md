# E6: Boomerang Skillshot

> 清单覆盖: E6 回旋弹道(去+回)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

### E6: 回旋弹道(去+回)

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_boomerang
    → EffectPresetType: CreateUnit
    → ProjectileDescriptor:
      - TrajectoryType: Boomerang
      - OutwardSpeed: 1200 cm/s
      - ReturnSpeed: 1500 cm/s
      - MaxRange: 1800 cm
      - OnHit: ApplyEffect(damage_effect)
      - DestroyOnHit: false  // 穿透
      - ReturnToCaster: true
```

**状态机**:
```
State: Outward
  - 每 tick 推进
  - 记录命中目标
  - 到达 MaxRange → 切换到 Return

State: Return
  - 每 tick 朝向 caster 推进
  - 记录命中目标 (避免重复)
  - 到达 caster → 销毁
```

**示例**: LoL Ahri Q, Sivir Q, GoW 斧头召回

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进 |
| TrajectoryType | ❌ 需新增 | 支持 Boomerang 模式 |
| State machine | ❌ 需新增 | Outward/Return 切换 |
| HitEntities tracking | ⚠️ 需扩展 | 避免重复命中 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Boomerang trajectory | P2 | 去回两阶段状态机 |
| ReturnToCaster flag | P2 | 自动追踪施法者 |
| Separate speeds | P2 | 去程/回程速度可配置 |
