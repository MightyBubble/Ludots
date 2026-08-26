# tag-01 reference · Tag 表示与状态

> 现状参考。第一性需求见 [tag-01 PRD](../prd/tag-01-basics.md)；配置说明见 [tag-01 配置说明](../config/tag-01-basics.md)。

## 1. 现状快照

- 表示：256-bit 位图在场集 + 层数容器（≤16 种计数）+ 帧快照 + 有效缓存（规则禁用后）。定时缓冲组件存在，但它是**能力时间轴 TagClip 的内部实现载体**：唯一运行时写入方为能力执行系统（带预约-回滚），无任何独立于能力/效果的时效配置面。
- 名字首现即注册（上限 256，`0` 为无效兼监听通配）仅限**玩法 Tag 配置面**（grantedTags / TagClip / blockTags / 规则等）；效果 `categories` 与技能 `categories`、表现事件 key 分表注册，不占玩法位图。授予来自效果 grantedTags 与技能时间轴 TagClip；判定默认有效视角。
- 变化走脏标记 → 帧末快照对比 → 下一拍事件（见 tag-03 reference）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 在场集位图 | src/Core/Gameplay/GAS/Components/GameplayTagContainer.cs |
| 层数容器 | src/Core/Gameplay/GAS/Components/TagCountContainer.cs |
| 限时退层（TagClip 实现载体；唯一写入方=能力执行系统，预约-回滚） | src/Core/Gameplay/GAS/Components/TimedTagBuffer.cs；src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:1226-1252、TimedTagExpirationSystem.cs |
| 有效缓存 | src/Core/Gameplay/GAS/Components/GameplayTagEffectiveCache.cs |
| 统一操作入口与规则事务 | src/Core/Gameplay/GAS/TagOps.cs、TagRuleTransaction.cs |
| 名字注册 | src/Core/Gameplay/GAS/Registry/TagRegistry.cs |
| 容量常量 | src/Core/Gameplay/GAS/GasConstants.cs（见事实页） |

**相关文档**：[tag-01 PRD](../prd/tag-01-basics.md) · [tag-02 reference](tag-02-rules.md)
