# E3: Cone Skillshot

> 清单覆盖: E3 锥形范围

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

### E3: 锥形范围

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → cone_search_effect
    → EffectPresetType: Search
    → BuiltinHandler: SpatialQuery
      - ShapeType: Cone
      - Range: 800 cm
      - Angle: 60 degrees
      - Direction: from input
      - Filter: Enemy
    → OnHit: FanOutApplyEffect(damage_effect)
```

**实现方式**:
1. **即时锥形检测**: `HandleSpatialQuery` 在 tick 0 执行
2. **角度判定**: `Vector2.Angle(toTarget, facingDir) <= coneAngle/2`
3. **距离判定**: `Distance(caster, target) <= range`

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| SpatialQuery handler | ✅ 已有 | 已支持 radius |
| Cone shape (QueryCone op 104) | ✅ 已有 | GraphOps.cs 已支持 QueryCone |
| Direction input | ✅ 已有 | targetType: Direction |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ~~Cone shape~~ | ~~P1~~ | ✅ 已有 — QueryCone (op 104) |
| ~~Angle parameter~~ | ~~P1~~ | ✅ 已有 — QueryCone 含 Angle 立即数 |
