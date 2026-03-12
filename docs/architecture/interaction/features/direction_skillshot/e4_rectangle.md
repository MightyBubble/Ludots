# E4: Rectangle Skillshot

> 清单覆盖: E4 矩形/线形范围

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

### E4: 矩形/线形范围

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → rectangle_search_effect
    → EffectPresetType: Search
    → BuiltinHandler: SpatialQuery
      - ShapeType: Rectangle
      - Length: 1200 cm
      - Width: 300 cm
      - Direction: from input
      - Filter: Enemy
    → OnHit: FanOutApplyEffect(damage_effect)
```

**实现方式**:
1. **矩形碰撞检测**:
   - 计算矩形 4 个顶点
   - 用 OBB (Oriented Bounding Box) 判定
2. **或**: 用多个圆形 overlap 近似矩形

**示例**: LoL Rumble R (矩形火焰带)

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| SpatialQuery handler | ✅ 已有 | 已支持 radius |
| Rectangle shape | ❌ 需新增 | OBB 碰撞检测 |
| Direction input | ✅ 已有 | selectionType: Direction |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Rectangle shape | P2 | SpatialQuery 添加 ShapeType.Rectangle |
| OBB collision | P2 | 矩形碰撞检测算法 |
| Length/Width params | P2 | 矩形尺寸配置 |
