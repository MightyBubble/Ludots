# Mechanism: D5 — Constrained Placement (约束区域放置)

> **Examples**: SC2 Warp Gate(须水晶塔能量场)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
- Presenter: GroundOverlay 根据 validation 结果显示绿色(合法)/红色(非法)

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| ValidationGraphId | ✅ 已有 |
| GraphExecutor.ExecuteValidation() | ✅ 已有 |
| GroundOverlay Presenter | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。需要配置具体的 validation graph 逻辑。
