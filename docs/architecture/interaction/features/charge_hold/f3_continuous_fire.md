# F3: 持续连射

> 清单编号: F3 | 游戏示例: OW Soldier/Bastion/Tracer

## 交互层

- **InputConfig**: ReactsTo = **Down**（持续，每帧/每周期触发）
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

按住期间持续向当前瞄准方向发射，松开即停止。

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
AbilityExecSpec:
  PeriodicSearch (period=3 ticks, auto-repeat while held):
    → Direction from continuous cursor tracking
    → LaunchProjectile each period
```

备选方案：

```
// 也可用 StartEnd 启动/停止射击循环
Down → 启动 toggle-like 射击循环:
  EffectClip: fire_loop (duration=Infinite)
    → OnPeriod (period=3 ticks):
      1. ReadBlackboard(aim_direction)
      2. LaunchProjectile(direction=aim_direction)

Up → 停止 fire_loop（RemoveEffect）
```

推荐使用 `heldPolicy: EveryFrame`；AbilityExecSystem 内置周期控制，每次 order 提交均触发一次子弹发射，天然对应连射节奏。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| 按住连发 | 技能执行 | 重复节奏写在技能 exec 上，输入侧只负责进入和离开手势状态 |
| AbilityExecSpec 周期触发 | ✅ 已有 | PeriodicSearch + LaunchProjectile |
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进+碰撞 |
| Cursor direction → Blackboard | ❌ P1 新增（参见 F1 需求） | 实时更新瞄准方向; CursorDirectionBlackboardWriter 尚未实现 |

## 新增需求

按住期间的重复写在技能 exec 的周期上。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
