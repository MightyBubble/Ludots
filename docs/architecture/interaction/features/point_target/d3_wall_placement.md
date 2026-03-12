# Mechanism: D3 — Wall Placement (放置墙体/地形阻挡)

> **Examples**: LoL Anivia W, Azir R, Dota Invoker Ice Wall

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
- Performer: `GroundOverlay` (Line, length, width, angle)

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| CreateUnit handler | ✅ 已有 |
| Collision 组件 | ✅ 已有 |
| Duration effect | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Wall entity template 规范 | P2 | 定义墙体 segment 的标准配置 |
