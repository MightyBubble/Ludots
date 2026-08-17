# fx-18 配置说明 · 参数化

> 配置写法与行为。第一性需求见 [fx-17 PRD](../prd/fx-14-config-params.md)；编辑器需求见 [UXD](../uxd/fx-14-config-params.md)；现状见 [reference](../reference/fx-14-config-params.md)。

## 1. 示例配置

自定义键三类型（真实条目，`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/effects.json`）：

```json
"configParams": {
  "showcase.config.power":       { "type": "Float", "value": 40 },
  "showcase.config.tier":        { "type": "Int", "value": 2 },
  "showcase.config.chainEffect": { "type": "EffectTemplate", "value": "Effect.GraphOps.Strike" }
}
```

保留键 `_ep.*` 与生命周期引用类型（真实条目：同上 mod 的 `Effect.GraphOps.Lifecycle`；`mods/showcases/moba_demo/MobaDemoMod` 的力参数同型）：

```json
"configParams": {
  "_ep.forceXAttribute":                { "type": "Float", "value": 250 },
  "_ep.forceXTargetAttrId":             { "type": "Attribute", "value": "Physics.ForceRequestX" },
  "_ep.targetEntityTemplate":           { "type": "EntityTemplate", "value": "GraphOps.Ally" },
  "_ep.lifecycleAttributeValueSource":  { "type": "LifecycleAttributeValueSource", "value": "Current" },
  "_ep.lifecycleAttribute0":            { "type": "Attribute", "value": "Health" }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| 键名 | 参数槽名；`_ep.` 前缀为引擎保留，其余自定义；加载期归一为键 id |
| `type` / `value` | 类型决定解析方式；引用类型解析失败即启动失败 |

七类型：`Float`/`Int` 写数值；`EffectTemplate`、`Attribute`、`ExchangeOperation`、`EntityTemplate`、`LifecycleAttributeValueSource`（仅 `Base`/`Current`）写注册名，分别依赖效果模板、属性（首现注册）、兑换操作、实体模板注册表。

`_ep.` 保留键全集（按消费域分组，值语义见各域分篇）：时长 `_ep.durationTicks/periodTicks`；力 `_ep.forceX/YAttribute`、`_ep.forceX/YTargetAttrId`；目标查询 `_ep.queryRadius/InnerRadius/HalfAngle/HalfWidth/HalfHeight/Length/Rotation/MaxTargets`、`_ep.targetPosX/Y`、`_ep.targetOriginX/Y`；派发 `_ep.payloadEffectId`；弹道 `_ep.projectileSpeed/Range/ArcHeight`、`_ep.impactEffectId`；造单位 `_ep.unitTypeId/Count/OffsetRadius`、`_ep.onSpawnEffectId`；兑换 `_ep.exchangeOperationId/ScopeKey`；生命周期 `_ep.targetEntityTemplate`、`_ep.lifecycleAttribute0..3`（容量 4）、`_ep.lifecycleAttributeValueSource`。

**合并语义**：施放侧 CallerParams 同键覆盖模板值（连类型一起），异键容量内追加；实体化效果把合并结果预合并存组件，Instant 内联路径每次现算。

## 3. 文件结构

`configParams` 是 `assets/GAS/effects.json` 效果条目的可选块（条目骨架@@fx2@@）。

## 4. 运行时加载效果

键经 ConfigKeyRegistry 归一为 int id；按类型把 value 解析为注册 id 或数值写入模板参数集。参数条数上限见[事实与取值表](../facts.md)；数值改动经工作台热通道为下次施放生效级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 `type` | 启动失败，列出七种合法类型 |
| 引用名未注册 | 启动失败，指明键与名字 |
| `LifecycleAttributeValueSource` 非 Base/Current | 启动失败 |
| 模板侧参数超上限（事实页） | 启动失败，指明容量 |
| caller 追加超容量 | 现状静默丢弃（治理中，见 spec E12） |

## 6. 实例

- 自定义键与效果引用：`mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/effects.json`
- `_ep.force*` 力参数：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`

**相关文档**：[fx-17 PRD](../prd/fx-14-config-params.md) · [attr-01 配置说明](attr-01-definition.md)
