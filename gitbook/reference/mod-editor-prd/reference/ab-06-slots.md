# ab-06 reference · 槽位系统

> 现状参考。第一性需求见 [ab-06 PRD](../prd/ab-06-slots.md)；配置说明见 [ab-06 配置说明](../config/ab-06-slots.md)。

## 1. 现状快照

- AbilityStateBuffer：CAPACITY=8，四数组（AbilityIds/TemplateIds/WorldIds/Versions）+ Count。
- GrantedSlotBuffer：8 槽 + 来源 tag 记账（SourceTagIds）；Grant（技能 id 或模板实体）/Revoke/RevokeBySource（按来源批量回收）；生产代码无写入口，仅测试使用。
- AbilityFormSlotBuffer：8 槽 SetOverride/ClearAll，每帧由形态路由系统重算。
- ItemGrantedSlotBuffer：8 槽，来源物品实体记账；InventoryEquipmentGrantSyncSystem 从物品 abilityGrants 整层重建（卸下即移除组件）。
- AbilitySlotResolver：四层 granted>itemGranted>form>base 逐层 HasOverride 短路；槽号越界 false；TryFindAbility 全槽扫描。
- 模板层：abilityIds ≤8 启动校验（超出抛错）、字符串解析为注册 id（未注册抛错）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 底座缓冲 | src/Core/Gameplay/GAS/Components/AbilityStateBuffer.cs:26-67 |
| 授予缓冲与回收 | AbilityStateBuffer.cs:76-174 |
| 形态槽缓冲 | AbilityStateBuffer.cs:181-242 |
| 四层解析器 | AbilityStateBuffer.cs:248-368（TryFindAbility :350 起） |
| 物品槽缓冲 | src/Core/Gameplay/Items/ItemComponents.cs:48-110 |
| 装备同步写入口 | src/Core/Gameplay/Items/InventoryEquipmentGrantSyncSystem.cs:80-123 |
| 物品授予解析 | src/Core/Gameplay/Items/ItemConfigLoader.cs:336-350 |
| 模板层解析与上限 | src/Core/Config/ComponentRegistry.cs:645-690 |
| 真实实例 | mods/showcases/rts_red_alert_like/.../Entities/templates.json；mods/showcases/item_system/.../Items/definitions.json |

**相关文档**：[ab-06 PRD](../prd/ab-06-slots.md) · [ab-07 reference](ab-07-form-sets.md)
