# E4: Rectangle Skillshot

> 清单覆盖: E4 矩形/线形范围

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| Rectangle shape (QueryRectangle op 105) | ✅ 已有 | GraphOps.cs 已支持 QueryRectangle |
| Direction input | ✅ 已有 | targetType: Direction |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| ~~Rectangle shape~~ | ~~P2~~ | ✅ 已有 — QueryRectangle (op 105) |
| ~~OBB collision~~ | ~~P2~~ | ✅ 已有 — QueryRectangle 内建 |
| ~~Length/Width params~~ | ~~P2~~ | ✅ 已有 — QueryRectangle 立即数 |
