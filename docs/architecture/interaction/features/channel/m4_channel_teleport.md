# Mechanism: M4 — Channel Teleport (引导TP: 传送读条)

> **Examples**: Dota TP Scroll, LoL Recall, SC2 Warp Gate, WoW炉石

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point** (传送目标) 或 **None** (回城)
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

**引导传送**:
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" + "teleporting", duration=180 ticks
  Item[1]: TagClip "rooted", duration=180 ticks
  Item[2]: EffectSignal @ tick 180 → teleport_effect
    → Phase Graph:
      1. ReadBlackboard(order_point)  // 或 fixed_base_position
      2. Teleport(self, target_position)
      3. AddVisualEffect("teleport_flash")
```

**打断机制**:
```
受到伤害 / CC:
  → RemoveTag("channeling")
  → InterruptAny → 传送失败
  → 技能进入冷却 (或部分冷却)
```

**视觉表现**:
```
Presenter: TeleportChannel(duration=180 ticks)
  → 螺旋光柱 / 读条特效
  → 完成时: 闪光传送
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| TagClip | ✅ 已有 |
| channeling tag | ✅ 已有 |
| rooted tag | ✅ 已有 |
| Teleport handler | ✅ 已有 |
| InterruptAny on tag loss | ✅ 已有 |
| Damage interrupt | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Damage interrupt channel | P1 | 受伤打断引导 |
| Partial cooldown on interrupt | P2 | 打断后部分冷却 |
