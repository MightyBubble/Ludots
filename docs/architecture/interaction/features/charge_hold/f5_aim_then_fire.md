# F5: 按住瞄准+另一键施放

> 清单编号: F5 | 游戏示例: GoW L2瞄准+R1/R2, OW各英雄

## 交互层

- **InputConfig**:
  - Aim 键: ReactsTo = **DownAndUp**
  - Fire 键: ReactsTo = **Down**
- **TargetMode**: **Direction**（Fire 键）
- **Acquisition**: **Explicit**

按住 Aim 键进入瞄准模式（可能切换相机、显示准星），在瞄准模式下按 Fire 键施放技能。

## Ludots 映射

```
L2 (Aim) mapping:
  actionId: "Aim"
  trigger: PressedThisFrame
  orderTypeKey: "aim_mode"
  selectionType: None
  isSkillMapping: false
  heldPolicy: StartEnd

R1/R2 (Fire) mapping:
  actionId: "Fire"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
  // 仅在 HasTag("aiming") 时有效 → precondition 检查
```

## 实现方案

```
Aim mode:
  Down → Order: activate_aim
    → AddTag("aiming")
    → 切换 camera (如 over-shoulder 视角)
    → 显示准星 UI

  Up → Order: deactivate_aim
    → RemoveTag("aiming")
    → 恢复默认 camera

Fire:
  precondition: HasTag("aiming")
  exec:
    → ReadBlackboard(aim_direction)
    → LaunchProjectile(direction=aim_direction)
```

**关键点**：
- Fire 技能的 `AbilityActivationRequireTags` 包含 `"aiming"`，确保只在瞄准模式下可用
- Aim mode 可触发 camera 切换（通过 CameraBlendSystem 或 CameraFocusStack）
- 松开 Aim 键后，Fire 键失效（precondition 不满足）

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| HeldPolicy.StartEnd | ✅ 已有 | Aim 键生成 .Start/.End order 对 |
| AbilityActivationRequireTags | ✅ 已有 | Fire 技能门控在 "aiming" tag |
| GameplayTagContainer | ✅ 已有 | 标签授予/移除 |
| CameraBlendSystem | ✅ 已有 | 相机切换（参见 CameraAcceptanceMod） |
| Cursor direction → Blackboard | ❌ P1 新增（参见 F1 需求） | 实时更新瞄准方向; CursorDirectionBlackboardWriter 尚未实现 |

## 新增需求

无。所有依赖组件已实现；Aim mode 的 camera 切换可通过 CameraBlendSystem 或 CameraFocusStack 实现。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
- `mods/fixtures/camera/CameraAcceptanceMod/` — 相机系统验收测试
