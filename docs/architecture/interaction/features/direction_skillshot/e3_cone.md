# E3: Cone Skillshot

> 清单覆盖: E3 锥形范围

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

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
| Cone shape | ⚠️ 需扩展 | 需添加 Angle 参数 |
| Direction input | ✅ 已有 | selectionType: Direction |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Cone shape support | P1 | SpatialQuery 添加 ShapeType.Cone |
| Angle parameter | P1 | 锥形角度配置 |
