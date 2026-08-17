# fx-19 reference · 弹道

> 现状参考。第一性需求见 [fx-18 PRD](../prd/fx-15-projectile.md)；配置说明见 [fx-18 配置说明](../config/fx-15-projectile.md)。

## 1. 现状快照

- loader：projectile 块仅 LaunchProjectile + Instant；travelMode 仅 Direction/TrackTarget（Legacy 显式报错）、impactPolicy 仅 DestroyOnFirstHit/ContinueOnHit（Legacy 报错）；hitEffect 必填、collisionHalfWidth>0、collisionRelationFilter 必填、collisionExcludeSource 走 RejectOptionalFalse、maxHitCount 限 1..ProjectileState.HitHistoryCapacity（=32）；impactEffect/presentationEffect 可选（空串报错）。
- runtime：HandleCreateProjectile 解析原点/目标点/方向（Direction 无方向抛错），组装 ProjectileState 经 RuntimeEntitySpawnRequest 入队；事务内 StageSpawnRequest，队列满抛错；speed<=0 或源死亡静默返回。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:452-464 |
| projectile 编译 | EffectTemplateLoader.cs:869-976 |
| Legacy 报错（travel/impact） | EffectTemplateLoader.cs:944-976 |
| maxHitCount 区间 | EffectTemplateLoader.cs:897-904 |
| collisionExcludeSource 仅 true | EffectTemplateLoader.cs:1785-1791 |
| 发射处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:273-356 |
| 命中历史容量 | src/Core/Gameplay/GAS/ProjectileState.cs:9 |

**相关文档**：[fx-18 PRD](../prd/fx-15-projectile.md) · [fx-18 配置说明](../config/fx-15-projectile.md)
