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

```json
{
  "abilities": [
    {
      "id": "deflect_press",
      "inputBinding": "E",
      "inputMode": "Press",
      "onActivate": {
        "effects": [
          { "type": "AddTag", "tag": "deflecting", "duration": 10 },
          { "type": "PlayAnimation", "id": "deflect_stance" },
          { "type": "AddTag", "tag": "deflect_cooldown", "duration": 60 }
        ]
      }
    }
  ],
  "projectileHitLogic": {
    "onHit": {
      "conditionalBranch": {
        "condition": { "type": "TargetHasTag", "tag": "deflecting" },
        "ifTrue": {
          "effects": [
            { "type": "ReverseProjectileOwnership", "projectile": "self" },
            { "type": "ReverseProjectileDirection", "projectile": "self", "direction": "targetFacing" },
            { "type": "RemoveTag", "target": "hitTarget", "tag": "deflecting" },
            { "type": "FireEvent", "event": "projectile_deflected" }
          ]
        },
        "ifFalse": {
          "effects": [
            { "type": "ApplyDamage", "target": "hitTarget", "amount": "self.damage" }
          ]
        }
      }
    }
  }
}
```
