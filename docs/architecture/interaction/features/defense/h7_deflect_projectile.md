# H7: Deflect / Reflect Projectile

## Overview

A defensive ability that redirects incoming projectiles back toward their source or in a chosen direction. The player must time the deflect input to coincide with the projectile's arrival. Successful deflection negates the incoming damage and sends the projectile back as a new attack. Reference: OW Genji deflect, LoL Yasuo wind wall.

## User Experience

- An enemy fires a projectile toward the player
- Player presses the deflect button (e.g., E) at the moment the projectile is about to hit
- If timing is correct: a deflect animation plays, the projectile reverses direction, and travels back toward the original attacker (or in the direction the player is facing)
- The reflected projectile retains its original damage and properties, now targeting the enemy
- If timing is early or late: the deflect whiffs and the projectile hits normally

## Implementation

The deflect ability activates a short `deflecting` tag window. Incoming projectiles check for this tag before applying damage. If the tag is present, the projectile's ownership and direction are reversed:

```
deflect_press:
  inputBinding: E (press)
  onActivate: AddTag("deflecting", duration=10 ticks)
              + PlayAnimation("deflect_stance")

projectile_on_hit:
  precondition: target.HasTag("deflecting")
  effect: ReverseProjectileOwnership(self)
          + ReverseProjectileDirection(self, target.facingVector)
          + RemoveTag(target, "deflecting")
          + FireEvent("projectile_deflected")

projectile_on_hit_fallback:
  precondition: NOT target.HasTag("deflecting")
  effect: ApplyDamage(target, self.damage)
```

**Direction control**: The reflected projectile's new direction is determined by the player's facing vector at the moment of deflection. Alternatively, it can be hardcoded to return directly to the original source.

**Multi-projectile deflection**: If multiple projectiles arrive during the `deflecting` window, all are deflected. The window duration controls how many can be caught.

**Cooldown**: A `deflect_cooldown` tag is applied after activation to prevent spam.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | `deflecting` window closes automatically after N ticks |
| Projectile ownership transfer | ⚠️ **Required** | Change projectile's source entity to the deflecting player |
| Projectile direction reversal | ⚠️ **Required** | Reverse or redirect projectile velocity vector |
| Projectile hit detection | ✅ Existing | Check for `deflecting` tag before applying damage |
| FireEvent("projectile_deflected") | ✅ Existing | Trigger VFX, audio, and UI feedback |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/deflect_effects.json) ===
[
  {
    // 反射窗口 Buff：授予 deflecting Tag
    "id": "Effect.Deflect.WindowBuff",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 10 },
    "grantedTags": [
      { "tag": "Status.Deflecting", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 反射冷却 Buff
    "id": "Effect.Deflect.Cooldown",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 60 },
    "grantedTags": [
      { "tag": "Status.DeflectCooldown", "formula": "Fixed", "amount": 1 }
    ]
  }
]

// === AbilityExecSpec: 反射激活 ===
{
  "id": "Ability.Deflect.Press",
  "blockTags": ["Status.DeflectCooldown"],
  "exec": {
    "totalTicks": 10,
    "items": [
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Deflect.WindowBuff" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Deflect.Cooldown" }
    ]
  }
}

// === ResponseChainListener：弹道命中时反射 ===
// 监听 projectile_hit 事件，当持有 Status.Deflecting 时触发
{
  "response_chain_listeners": [
    {
      // Hook: 取消弹道命中伤害
      "eventTagId": "projectile_hit",
      "responseType": "Hook",
      "priority": 300,
      "responseGraphId": "Graph.Deflect.CheckWindow"
      // Graph.Deflect.CheckWindow:
      //   HasTag          E[0], Status.Deflecting → B[0]
      //   JumpIfFalse     B[0], END
      //   (Hook 生效 → 取消弹道伤害)
    },
    {
      // Chain: 触发弹道反射处理
      "eventTagId": "projectile_hit",
      "responseType": "Chain",
      "priority": 301,
      "effectTemplateId": "Effect.Deflect.Redirect",
      "responseGraphId": "Graph.Deflect.CheckWindow"
    }
  ]
}

// === P1 BuiltinHandler 需求：弹道所有权/方向反转 ===
// 弹道反射需要新增 BuiltinHandler：
//
// BuiltinHandlerId.RedirectProjectile (P1):
//   - 修改 Projectile 的 Owner 为反射者
//   - 反转 Projectile 速度方向（使用反射者 facingVector 或直接 180° 反转）
//   - 保留原弹道的 impactEffect 和伤害参数
//
// 该 Handler 在 Effect.Deflect.Redirect 的 OnApply phase 中调用：
{
  "id": "Effect.Deflect.Redirect",
  "presetType": "None",
  "lifetime": "Instant",
  "builtinHandlers": {
    "OnApply": "RedirectProjectile"    // P1: 需在 BuiltinHandlerRegistry 注册
  },
  "configParams": {
    "directionMode": { "type": "int", "value": 0 }
    // 0 = 使用反射者朝向, 1 = 直接反转 180°
  }
}

// === 事件通知 ===
// Graph.Deflect.CheckWindow 在 Hook 成功后发送事件：
//   SendEvent  "projectile_deflected"   // 触发 VFX/音效反馈

// 注: 多个弹道在 deflecting 窗口内命中时，每个都会触发独立的
// ResponseChain 处理，实现多弹道反射。窗口在所有弹道处理完毕
// 或 duration 到期后自动关闭。
```
