# Mechanism: I10 滑翔 (Glide)

> 按住按键减缓下落速度进行滑翔

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp (按住)**
- **TargetMode**: **None**
- **Acquisition**: **Implicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillSpace"
  trigger: Down + WhileHeld + Up
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
  precondition: is_airborne == true
```

## 实现方案

```
Down 阶段 (仅在空中时有效):
  1. Precondition: HasTag("airborne") 或 velocity.y < 0
  2. AddTag("gliding")
  3. 修改 gravity_scale → 0.1 (减缓下落)
  4. 修改 fall_speed_max → slow_fall_speed

WhileHeld:
  PeriodicEffect (period=1 tick):
    → 保持 gravity_scale = 0.1
    → 可应用水平加速 (根据输入方向)

Up 阶段:
  1. RemoveTag("gliding")
  2. 恢复 gravity_scale → 1.0
```

- 属于被动状态机模式
- 需要与 Physics2D 层协作处理重力缩放

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 重力缩放属性 (gravity_scale) | P3 | 临时减缓下落 |
| 空中状态检测 | P3 | is_airborne precondition |
| WhileHeld 输入持续触发 | P3 | 按住期间持续修改物理参数 |
