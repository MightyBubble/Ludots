# Mechanism: I5 摆荡 (Swing)

> 按住按键在锚点上进行物理摆荡 (Spider-Man)

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **None**
- **Acquisition**: **Implicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
Down 阶段:
  1. 搜索头顶锚点 (SpatialQuery, tag="swing_point", cone=upward)
  2. 创建 tether effect (连接 caster 和 anchor)
  3. 添加 "swinging" tag

WhileHeld (effect tick):
  PeriodicEffect (period=1 tick):
    → 应用摆荡物理 (pendulum force)
    → 计算绳长约束
    → 更新速度向量

Up 阶段:
  1. 移除 tether effect
  2. 移除 "swinging" tag
  3. 保留当前惯性速度
```

- **需要**: Physics2D 层支持 pendulum/swing 约束
- 超出 GAS 范围, 属于物理层扩展

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 摆荡物理 (Physics2D 层) | P3 | pendulum constraint, rope physics |
| Tether effect 组件 | P3 | 连接两个实体的约束 |
| 惯性速度保留 | P3 | 释放时保持物理速度 |
