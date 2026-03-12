# Mechanism: K7 — Controllable Projectile (释放可操控投射物)

> **Examples**: OW Junkrat轮胎(释放后切换视角操控), Dota Morphling转换

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → spawn_controllable_projectile
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "riptire_projectile"
      position: caster_position
      ownerId: caster
      controllable: true
      tags: ["controllable_projectile", "bouncing"]
      attributes:
        move_speed: 800
        health: 1

  Item[1]: EffectSignal → transfer_input_focus
    → BuiltinHandler: TransferInputFocus
    → target: projectile_entity
    → InputProfile: "projectile_control"
      → move: WASD → projectile move
      → action: Space → detonate
```

**操控状态**:
```
Player input → remapped to projectile_entity movement
Camera follows projectile_entity (camera focus switch)

On detonate (Space / collision):
  → AoE explosion at projectile position
  → DestroyEntity(projectile)
  → RestoreInputFocus(caster)
  → RestoreCameraFocus(caster)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| TransferInputFocus | ❌ 需新增 |
| Camera focus switch | ❌ 需新增 |
| Projectile physics | ✅ 已有 |
| Input profile system | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Input focus 切换机制 | P2 | 将输入从玩家转移到召唤物 |
| Camera focus switch | P2 | 摄像机跟随投射物 |
| Input profile remapping | P2 | 不同实体不同控制映射 |
| Restore on entity death | P2 | 投射物销毁时恢复控制 |
