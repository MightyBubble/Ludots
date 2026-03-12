# N8: Soft Lock-On

## 1. User Experience

After selecting a target (explicitly or via context scoring), caster automatically rotates to face target during attack animations, providing "sticky" targeting without hard lock.

**Examples**: Dark Souls/Sekiro/God of War soft lock during attacks

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored or Explicit
- **TargetFilter**: Enemy

## 3. Implementation Strategy

**Tag-Based Approach** (no new systems needed):

1. Ability adds `SoftLocked` tag to caster on activation
2. Tag includes target entity reference in component data
3. Animation/movement system reads tag and rotates caster toward target
4. Tag removed on ability completion or target death

**Component Structure**:
```csharp
public struct SoftLockComponent
{
    public Entity Target;
    public Fix64 RotationSpeed;  // degrees per tick
    public Fix64 MaxAngle;       // max rotation per frame
}
```

**Ability Configuration**:
```json
{
  "ability_id": "locked_attack",
  "on_activate_effects": [
    {
      "effect_type": "AddTag",
      "tag": "SoftLocked",
      "duration": 60,
      "component_data": {
        "target": "$context.target",
        "rotation_speed": 720,
        "max_angle": 45
      }
    },
    {
      "effect_type": "Damage",
      "target": "$context.target"
    }
  ]
}
```

## 4. Tag/Effect/Attribute Usage

**Tags**:
- SoftLocked — caster state during attack
- Enemy — target filter

**Attributes**:
- None

**Effects**:
- AddTag(SoftLocked) — applied on ability activation
- Standard damage/knockback effects

## 5. Integration Points

**Input Layer**:
- `ContextScoredResolver` or explicit targeting provides target entity
- Target stored in `EffectContext`

**Animation/Movement System**:
- Queries entities with SoftLocked tag
- Reads target from component
- Applies rotation toward target each frame:
  ```csharp
  var targetDir = target.position - caster.position;
  var desiredAngle = Atan2(targetDir.Y, targetDir.X);
  var currentAngle = caster.rotation;
  var delta = ClampAngle(desiredAngle - currentAngle, -maxAngle, maxAngle);
  caster.rotation += delta;
  ```

**GAS Layer**:
- Tag automatically removed after duration
- Tag removed if target dies (via effect removal condition)
