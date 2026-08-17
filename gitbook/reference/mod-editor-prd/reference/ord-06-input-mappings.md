# ord-06 reference · 输入映射

> 现状参考。第一性需求见 [ord-06 PRD](../prd/ord-06-input-mappings.md)；配置说明见 [ord-06 配置说明](../config/ord-06-input-mappings.md)。

## 1. 现状快照

- 根字段：`interactionMode`（TargetFirst/SmartCast/AimCast/SmartCastWithIndicator/ContextScored/PressReleaseAimCast）/ `mappings[]` / `groupMoveTargetLayout`（None 或 Grid：spacingCm+orderTypeKeys）/ `userOverrides`（enabled + persistPath，默认 `user://input_preferences.json`）。
- mapping 字段：actionId、trigger（4 值 + doubleTapWindowSeconds）、`orderTypeKey` 或 `actorOrderRouting`（candidates：orderTypeKey/priority/match{requiredAllTags, blockedAnyTags, abilitySlotIndex, abilityIdKey, abilityIdKeySuffix}/targetType）、argsTemplate i0-i3/f0-f3、requireTarget、actor/targetCollectionKey、targetType（None/Position/Entity/Entities/Direction/Vector/HoveredEntityOrPosition；Ground 为过时别名）、modifierBehavior 4 值、isSkillMapping、heldPolicy、castModeOverride、auto/cursorTargetPolicy+Range。
- 校验：actionId 全局唯一；直连与路由互斥；routing 禁 isSkillMapping 与 Entities；技能映射须 i0 非负；auto/cursor 互斥范围>0；Grid 校验。
- 运行：SetInteractionMode 可换全局模式；生效模式 = CastModeOverride ?? 全局；模式分派经 CommandIntentArbiter.ResolveActiveCommandIntent 帧路由。
- 根 `assets/Input` **无此文件**——映射全部由 mod 携带；mod 缺文件仅日志跳过（不 fail-fast）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 根/映射/布局/覆写字段 | src/Core/Input/Orders/InputOrderMapping.cs:16-42,221-239,451-462 |
| 校验链 | src/Core/Input/Orders/InputOrderMappingLoader.cs:99-236 |
| 生效模式与帧路由调用点 | src/Core/Input/Orders/InputOrderMappingSystem.cs:1650 |
| 真实例 | mods/showcases/rts_demo/RtsDemoMod/assets/Input/input_order_mappings.json |

**相关文档**：[ord-06 PRD](../prd/ord-06-input-mappings.md) · [input-01 reference](input-01-command-intent.md)
