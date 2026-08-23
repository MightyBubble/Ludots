# fx-16 reference · 造单位

> 现状参考。第一性需求见 [fx-16 PRD](../prd/fx-16-unit-creation.md)；配置说明见 [fx-16 配置说明](../config/fx-16-unit-creation.md)。

## 1. 现状快照

- loader：unitCreation 块仅 CreateUnit + Instant；unitType 与 templateId 恰选其一（unitType 首现注册 UnitTypeRegistry）；Scatter 禁 facingPattern/placementRadiusCm/placementStartAngleDeg 且 offsetRadius 非负；Circle 禁 offsetRadius、placementRadiusCm 必填正数、placementStartAngleDeg 必填；facingPattern 缺省 PreserveTemplate；copySourcePlayerOwner/linkSourceAsParent 走 RejectOptionalFalse；onSpawnEffect 可选（未注册报错）。
- runtime：HandleCreateUnit 按 count 循环计算摆放与朝向，入队 Template/UnitType 两种 Kind 的 spawn 请求；CopySourceTeam 固定为 1；事务内 StageSpawnRequest，队列满抛错；count<=0 或源死亡静默返回。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:466-483 |
| unitCreation 编译与互斥 | EffectTemplateLoader.cs:978-1042 |
| RejectOptionalFalse | EffectTemplateLoader.cs:1785-1791 |
| 生成处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:358-415 |
| unitType 注册表 | src/Core/Gameplay/GAS/Registry/UnitTypeRegistry.cs:7 |

**相关文档**：[fx-16 PRD](../prd/fx-16-unit-creation.md) · [fx-16 配置说明](../config/fx-16-unit-creation.md)
