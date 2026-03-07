# Feature: Point Target Abilities (D1–D8)

> 清单覆盖: D1 圆形AoE, D2 环形, D3 墙体, D4 全图点选, D5 约束放置, D6 传送到点, D7 延迟AoE, D8 持续区域

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
  argsTemplate: { i0: 1 }
```

OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

### D1: 圆形AoE

```
AbilityExecSpec:
  Item[0]: EffectSignal → aoe_damage
    → EffectPresetType: Search
    → Handler: SpatialQuery
    → Graph: QueryRadius(origin=order_position, radius=250cm, filter=Enemy)
    → FanOutApplyEffect(damage_template)
```

- Performer: `GroundOverlay` (Circle, radius=250, color=red)

### D2: 环形/甜甜圈

```
Phase Graph:
  1. QueryRadius(origin, outer_radius)
  2. QueryFilterNotRadius(inner_radius)  // 排除内圈
  → FanOutApplyEffect(ring_damage)
```

- **需要**: `QueryFilterNotRadius` 或 `QueryRing` Graph op (排除内圆)
- **替代**: 用两次 QueryRadius 然后 list 差集

### D3: 放置墙体

```
实现:
  1. EffectSignal → create_wall_effect
  2. CreateUnit handler → spawn wall_segment entities (N个连续)
  3. Wall segments 有:
     - Collision 组件 (阻挡寻路)
     - Duration effect (生命周期)
     - OnExpire → 销毁 entities
```

- 已有: `CreateUnit` handler, 需配置 wall entity template

### D4: 全图点选(小地图)

```
InputOrderMapping:
  selectionType: Position
  // 唯一差别: Range 不做限制, 或 Range=Global
  // 小地图点击: 需要 InputOrderMappingSystem 支持 minimap click → world position 转换
```

- **需要**: Minimap click → world position 转换 (Adapter 层, 非核心)

### D5: 约束放置

```
实现:
  1. Order 提交时 ValidationGraphId 校验位置合法性
  2. OrderTypeConfig.ValidationGraphId → Graph:
     a. 读取 order position
     b. 检查是否在 power_field tag 区域内
     c. B[0] = valid/invalid
  3. 无效则 Order 被拒绝 (OrderSubmitResult.Blocked)
```

- 已有: `OrderTypeConfig.ValidationGraphId`, `GraphExecutor.ExecuteValidation()`

### D6: 传送到指定点

```
AbilityExecSpec:
  Item[0]: TagClip "channeling" @ tick 0, duration=180 ticks (3s channel)
  Item[1]: EffectSignal @ tick 180 → teleport_effect
    → Phase Graph: 设置 caster position = order_position
```

### D7: 延迟AoE

```
AbilityExecSpec:
  Item[0]: EffectClip @ tick 0, duration=90 ticks (1.5s delay)
    → EffectTemplate: lifetime=90 ticks
    → OnExpire Phase:
      1. QueryRadius(stored_position, radius)
      2. FanOutApplyEffect(delayed_damage)
```

- Performer: GroundOverlay(Circle, growing opacity) 在 1.5s 内渐显

### D8: 持续区域

```
AbilityExecSpec:
  Item[0]: EffectClip @ tick 0
    → EffectPresetType: PeriodicSearch
    → Period: 30 ticks
    → Radius: 200cm
    → Position: stored from order (不跟随施法者)
    → OnPeriod: FanOutApplyEffect(dot_tick)
    → Duration: 300 ticks
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| Search/PeriodicSearch preset | ✅ 已有 |
| QueryRadius Graph op | ✅ 已有 |
| CreateUnit handler | ✅ 已有 |
| ValidationGraphId | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| QueryRing / QueryFilterNotRadius op | P2 | D2 |
| Minimap click → world position | P3 | D4 (Adapter层) |
| Wall entity template 规范 | P2 | D3 |

## Performer 集成

| 场景 | PerformerVisualKind | 参数 |
|------|-------------------|------|
| D1 圆形AoE指示器 | GroundOverlay(Circle) | radius, color, opacity |
| D2 环形指示器 | GroundOverlay(Ring) | inner_radius, outer_radius |
| D3 墙体预览 | GroundOverlay(Line) | length, width, angle |
| D7 延迟AoE生长 | GroundOverlay(Circle) | opacity ← time-based ramp |
| D8 持续区域 | GroundOverlay(Circle) | radius, pulsing alpha |
