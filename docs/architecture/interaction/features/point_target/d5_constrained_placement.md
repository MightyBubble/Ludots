# Mechanism: D5 — Constrained Placement (约束区域放置)

> **Examples**: SC2 Warp Gate(须水晶塔能量场)

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
  1. Order 提交时 ValidationGraphId 校验位置合法性
  2. OrderTypeConfig.ValidationGraphId → Graph:
     a. 读取 order position
     b. 检查是否在 power_field tag 区域内
     c. B[0] = valid/invalid
  3. 无效则 Order 被拒绝 (OrderSubmitResult.Blocked)
```

- 已有: `OrderTypeConfig.ValidationGraphId`, `GraphExecutor.ExecuteValidation()`
- Performer: GroundOverlay 根据 validation 结果显示绿色(合法)/红色(非法)

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| ValidationGraphId | ✅ 已有 |
| GraphExecutor.ExecuteValidation() | ✅ 已有 |
| GroundOverlay Performer | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。需要配置具体的 validation graph 逻辑。
