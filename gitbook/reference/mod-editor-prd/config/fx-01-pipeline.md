# fx-04 配置说明 · 效果执行管线总览

> 配置写法与行为。第一性需求见 [fx-03 PRD](../prd/fx-01-pipeline.md)；编辑器需求见 [UXD](../uxd/fx-01-pipeline.md)；现状见 [reference](../reference/fx-01-pipeline.md)。

## 1. 示例配置

效果表全字段@@fx1@@；本篇用同一张表里两种归宿的条目展示管线两端（rts 底座，真实）：

```json
[
  { "id": "Effect.Rts.RedAlert.CostPowerPlantStep", "tags": ["Effect.Rts.RedAlert.Cost"],
    "presetType": "InstantDamage", "lifetime": "Instant", "participatesInResponse": false,
    "modifiers": [ { "attribute": "Credits", "op": "Add", "value": -62.5 } ] },
  { "id": "Effect.Rts.RedAlert.Construction", "tags": ["Effect.Rts.RedAlert.Construction"],
    "presetType": "Buff", "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame" } }
]
```

读法：代价条目当帧内联走完提案与应用；建造 Buff 实体化进目标容器，由存活段每 tick 推进，45 tick 后过期。

## 2. 字段与行为

| 字段规则 | 这样配会产生什么效果 |
|---|---|
| `lifetime` 三值 | 管线路由依据：Instant 同帧内联，After/Infinite 实体化（fx-06） |
| `duration.periodTicks` | 存活段周期节拍，>0 才有周期相位（fx-06） |
| `presetType` | 在 preset_types 之后解析——加载序即引用许可序（fx-05） |
| `grantedTags` / `phaseListeners` | 随效果实体生灭的资源，移除时统一回收（fx-16 / fx-10） |
| `stack` | 同类效果在目标容器内的合并规则（fx-15） |

## 3. 文件结构

`assets/GAS/effects.json`，分片目录 目录条目 `GAS/effects/`（分片目录，根数据为空）；mod 侧放各自 `assets/GAS/` 下，跨 mod 同 id 深合并（cfg-05）。加载序在 graphs、preset_types 之后。

## 4. 运行时加载效果

效果表加载时逐条注册模板；全部加载器跑完后注册表 Finalize（此后拒绝注册、重复报冲突）；随后为每模板编译执行计划，四个相位窗口全部 finalized 才放行。运行期循环每帧切片：提案段循环消化请求直到无新增，再进存活段，最后消化存活段新引发的请求。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Finalize 后注册新模板 | 启动失败 |
| 模板 id 重复 | 启动失败，报冲突 |
| 执行计划任一窗口未 finalized | 启动失败 |
| 子系统超耗毫秒预算 | 运行期报错 |
| 事务提交失败 | 整体回滚并上抛 |

## 6. 实例

- `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（10 条：代价、建造、训练、部署）
- `assets/GAS/effects.json`（核心表现状仅 1 条，见 todo/effect.md E1）

**相关文档**：[fx-03 PRD](../prd/fx-01-pipeline.md) · [fx-04 配置说明](fx-02-template.md) · [rt-02](rt-02-budgets.md)
