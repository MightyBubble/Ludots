# Mechanism: M5 — Beam Sustained (持续光束: 按住维持连接)

> **Examples**: OW Zarya/Symmetra光束, LoL Vel'Koz R, Dota Disruptor Kinetic Field

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **Direction** (或 Unit)
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

**按住维持光束**:
```
AbilityExecSpec:
  Down → EffectClip: beam_effect
    period: 3 ticks
    duration: until_up

  OnPeriod Phase Graph:
    1. ReadBlackboard(cursor_direction)
    2. RaycastDirection(self, cursor_direction, max_range=1000cm)
    3. ForEach(hit_entity):
       → ApplyEffect(beam_damage_tick)

  Up → Stop effect
```

**光束追踪** (持续更新方向):
```
每帧 (InputSystem):
  → ReadCursorPosition()
  → WriteBlackboard(cursor_direction)
  → Beam renderer 读取方向并渲染
```

**视觉表现**:
```
Presenter: BeamRenderer(origin=caster, direction=cursor_direction)
  width: 50cm
  color: blue
  lifetime: 与 EffectClip 同步
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| DownAndUp InputConfig | ✅ 已有 |
| EffectClip (持续效果) | ✅ 已有 |
| RaycastDirection Graph op | ⚠️ 需扩展 |
| Cursor direction write | ⚠️ 需扩展 |
| BeamRenderer Presenter | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| RaycastDirection Graph op | P1 | 方向射线检测 |
| Cursor direction 持续写入 | P1 | 每帧更新方向 |
| BeamRenderer Presenter | P2 | 光束渲染 (表现层) |
| EffectClip until_up | P1 | 按住期间持续生效 |
