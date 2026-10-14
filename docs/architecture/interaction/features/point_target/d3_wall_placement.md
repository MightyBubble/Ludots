# Mechanism: D3 — Wall Placement (放置墙体/地形阻挡)

> **Examples**: LoL Anivia W, Azir R, Dota Invoker Ice Wall

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
  1. EffectSignal → create_wall_effect
  2. CreateUnit handler → spawn wall_segment entities (N个连续)
  3. Wall segments 有:
     - Collision 组件 (阻挡寻路)
     - Duration effect (生命周期)
     - OnExpire → 销毁 entities
```

- 已有: `CreateUnit` handler, 需配置 wall entity template
- Presenter: `GroundOverlay` (Line, length, width, angle)

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
