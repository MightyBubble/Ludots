# Mechanism: I9 墙壁攀爬 (Wall Climb)

> 被动/主动攀爬墙壁或垂直表面

## 交互层

- **InputConfig**: ReactsTo = **None (被动触发) / Down (主动)**
- **TargetMode**: **None**
- **Acquisition**: **Implicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: (被动, 无直接映射)
  trigger: 接触墙壁时自动触发
  orderTypeKey: "wallClimb"
  selectionType: None
  isSkillMapping: false

或主动版本:
  trigger: PressedThisFrame (接触墙壁时可用)
  selectionType: None
```

## 实现方案

```
被动触发:
  PeriodicSearch (period=1 tick):
    → 检测侧面碰撞 (wall contact)
    → if wall_contact → AddTag("wall_climbing")
    → 应用垂直移动速度 (修改 velocity.y)

主动攀爬:
  AbilityExecSpec:
    Item[0]: EffectSignal → wall_climb_effect
      → GrantedTags: ["wall_climbing", duration=60 ticks]
      → 修改物理参数: gravity_scale=0, 允许垂直输入映射
```

- 依赖 Physics2D 层提供墙壁接触检测
- 攀爬状态下需修改输入-速度映射
- 超出 GAS 范围, 部分逻辑属于 Physics2D 层

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 墙壁接触检测 (Physics2D) | P3 | 检测侧面碰撞 |
| 垂直移动输入重映射 | P3 | 攀爬状态下方向=垂直 |
| gravity_scale 组件属性 | P3 | 临时禁用重力 |
