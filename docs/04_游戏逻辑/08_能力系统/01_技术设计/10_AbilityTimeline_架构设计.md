---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - AbilityTimeline 时间轴驱动
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/05_Effect参数架构_架构设计.md
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/06_Effect生命周期Phase_架构设计.md
---

# AbilityTimeline 架构设计

# 1 Clip / Signal 模型

AbilityTimeline 的核心数据单位：

- **Clip**（持续的）：有 startTick + durationTicks。代表时间轴上一段跨度。
  - EffectClip：施加持续效果（可 override 模板参数）
  - TagClip：添加有过期时限的 tag
- **Signal**（瞬时的）：只有 tick。代表时间轴上一个点。
  - EffectSignal：施加瞬发效果
  - EventSignal：发射 GameplayEvent（驱动 Performer 表现）
  - GraphSignal：执行 Graph 程序
  - TagSignal：添加/移除 tag

# 2 Timeline 如何利用 CallerParams

Timeline 的 EffectClip 配置 `callerParams`，在构造 EffectRequest 时填入 CallerParams：

```json
{
  "clips": [
    {
      "tick": 0,
      "durationTicks": 300,
      "kind": "ApplyEffect",
      "effect": "Effect.DOT.Burn",
      "callerParams": {
        "_ep.durationTicks": { "type": "int", "value": 300 },
        "_ep.periodTicks": { "type": "int", "value": 50 },
        "tickDamage": { "type": "float", "value": 15.0 }
      }
    }
  ],
  "signals": [
    {
      "tick": 0,
      "kind": "Event",
      "event": "Event.Spell.Fireball.Cast"
    },
    {
      "tick": 150,
      "kind": "ApplyEffect",
      "effect": "Effect.Moba.Damage.Q"
    }
  ]
}
```

**Timeline 只做一件事**：把 clip 上配置的 key-value 填进 `EffectRequest.CallerParams`。不区分 `_ep.*` 保留键和用户键——全部等价透传。Effect 系统内部自行消费。

# 3 Timeline 与 Phase 管线的关系

```text
AbilityTimeline
    │
    │ 构造 EffectRequest（填 CallerParams）
    │ 构造 GameplayEvent（SendEvent）
    ▼
EffectRequestQueue / GameplayEventBus
    │
    ▼
EffectProposalProcessingSystem → EffectApplicationSystem → EffectLifetimeSystem
                                (Phase 执行, 自身行为 + Listener 反应)
```

Timeline **不参与** Phase 执行。它的职责是：
1. 按时钟推进，在正确的 tick 触发 Clip/Signal
2. 构造 EffectRequest + CallerParams，发布到 EffectRequestQueue
3. 发射 GameplayEvent，驱动 Performer 表现层

Phase 执行、自身行为（PhaseGraphBindings）、Listener 反应等逻辑完全在 Effect 系统内部处理。

# 4 Timeline 与 Presenter 联动

```text
Timeline EventSignal → GameplayEventBus.Publish(GameplayEvent)
                              ↓
                    GasPresentationEventBuffer 收集
                              ↓
                    PresentationEventStream 传递给表现层
                              ↓
                    PerformerRuleSystem 匹配规则
                              ↓
                    PresentationCommandBuffer 输出 PerformerCommand
```

Timeline 通过 `EventSignal` 在关键帧发射事件（如 `Event.Spell.Fireball.Cast`、`Event.Spell.Fireball.Impact`），Performer 规则体系根据事件 tag 匹配并执行视觉表现。逻辑与表现完全解耦。

# 5 混合时钟模式

AbilityTimeline 支持按不同时钟推进：

| 游戏模式 | Timeline.ClockId | 效果 |
|---|---|---|
| 即时制 MOBA | FixedFrame | 所有按帧推进 |
| 回合制 RPG | Turn | 所有按回合推进 |
| 混合（如 ATB） | Step | Step 驱动，可暂停 |

Per-Clip 可覆盖时钟（`ClipClockIds`），允许在同一 timeline 内混合不同时钟。

`GasController.RunUntilEffectWindowsClosed()` 确保在逻辑帧边界，所有已发出的 effect 处理完毕后才交出控制权。
