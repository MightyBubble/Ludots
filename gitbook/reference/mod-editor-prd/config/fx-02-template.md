# fx-01 配置说明 · 效果模板骨架

> 配置写法与行为。第一性需求见 [fx-01 PRD](../prd/fx-02-template.md)；编辑器需求见 [UXD](../uxd/fx-02-template.md)；现状见 [reference](../reference/fx-02-template.md)。

## 1. 示例配置

rts 底座效果表节选（真实）：一条 Buff、一条即时伤害：

```json
[
  { "id": "Effect.Rts.RedAlert.Construction",
    "tags": ["Effect.Rts.RedAlert.Construction"],
    "presetType": "Buff", "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame" },
    "grantedTags": [ { "tag": "State.Rts.RedAlert.Constructing", "formula": "Fixed", "amount": 1 } ] },
  { "id": "Effect.Rts.RedAlert.CostPowerPlantStep",
    "tags": ["Effect.Rts.RedAlert.Cost"],
    "presetType": "InstantDamage", "lifetime": "Instant", "participatesInResponse": false,
    "modifiers": [ { "attribute": "Credits", "op": "Add", "value": -62.5 } ] }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 模板身份；与所在条目 id 逐字一致，不一致启动失败 |
| `tags` | 效果身份标签，至多一枚 |
| `presetType` | 必填；按 preset_types 注册表→内建枚举序解析（fx-02） |
| `lifetime` | 必填，精确三值 Instant / After / Infinite（fx-03） |
| `participatesInResponse` | 必填布尔；false 的效果不收响应链回应（fx-06） |
| `duration` | 对象块；规则矩阵见 fx-03 |
| `expireCondition` | 可选块 kind/tag/sense；独立于时长的过期条件（fx-03） |
| 17 个组件块 | modifiers、targetQuery、targetFilter、targetDispatch、configParams、grantedTags、phaseGraphs、phaseListeners、stack、projectile、unitCreation、displacement、relation、progression、submitOrderFromBlackboard 等，分篇见 fx-03 起 |
| `stack` | 可选；三字段全必填（fx-11） |

跨字段规则：modifiers 容量与 ApplyForce2D 预留见事实页；Instant 效果禁带 phaseListeners；displacement/relation/progression/projectile/unitCreation/submitOrderFromBlackboard 六块只在对应 presetType 下合法且必须携带。

禁用字段：顶层 `period`（写进 duration 块）、标量 `duration`、`lifecycleDeploy`（部署链路走 configParams 保留键与 preset 图，见 fx-22）。

## 3. 文件结构

`assets/GAS/effects.json` 与分片 目录条目 `GAS/effects/`（分片目录，根数据为空）；mod 侧同路径，跨 mod 同 id 深合并（cfg-05）。加载序在 preset_types 之后。

## 4. 运行时加载效果

逐条解析注册；presetType 解析失败即启动失败。全部加载器跑完后 Finalize 冻结注册表，随后编译执行计划——四窗口全 finalized 才放行。热通道：工作台可热替换白名单字段（时长、周期、首修改器数值、弹道效果引用、槽 0 固定授予 tag），改其余字段重启生效。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| id 双写不一致 / tags 超一枚 | 启动失败，指明条目 |
| presetType 缺失或未注册 | 启动失败 |
| lifetime 非三值 / 标量 duration | 启动失败 |
| 非法组件块组合（如 Instant 带 phaseListeners） | 启动失败 |
| Finalize 后注册、重复 id | 启动失败，报冲突 |
| 热替换白名单外字段 | 拒绝并提示重启级 |

## 6. 实例

- `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（51 条，块用法最全）

**相关文档**：[fx-01 PRD](../prd/fx-02-template.md) · [fx-02 配置说明](fx-03-preset-types.md) · [ed-02](ed-02-hot-apply.md)
