# fx-04 配置说明 · 八相位执行

> 配置写法与行为。第一性需求见 [fx-04 PRD](../prd/fx-05-phases.md)；编辑器需求见 [UXD](../uxd/fx-05-phases.md)；现状见 [reference](../reference/fx-05-phases.md)。

## 1. 示例配置

`phaseGraphs` 按相位挂图（blacksmith 演示 mod，真实）：

```json
[
  { "id": "Effect.Showcase.Blacksmith.RandomDrift", "presetType": "DoT",
    "lifetime": "Infinite", "participatesInResponse": false,
    "duration": { "durationTicks": 0, "periodTicks": 60, "clockId": "FixedFrame" },
    "phaseGraphs": { "OnPeriod": { "post": "Graph.Showcase.Blacksmith.RandomDrift" } } }
]
```

教学骨架（三槽与跳过，仓库暂无同构实例）：

```json
"phaseGraphs": {
  "OnApply":  { "pre": "Graph.Example.PreBuff", "main": "Graph.Example.MainBuff" },
  "OnExpire": { "post": "Graph.Example.Cleanup", "skipMain": true }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| 相位键 OnPropose…OnRemove | 八相位任一可挂图；执行序固定不可重排 |
| `pre` / `main` / `post` | 槽内图 id；同相位按前置→主→后置执行 |
| `main` | 主槽权威；与 preset 默认处理器互斥出现（有 main 即不回落） |
| `skipMain: true` | 跳过主槽（不执行默认处理器）；与 main 同写报错 |
| 图的 kind | OnPropose 须 Validation、其余相位 Effect（fx-05） |

每模板绑定步上限 = 8 相位 × 3 槽（数值见事实页推导）。

## 3. 文件结构

`phaseGraphs` 是效果模板顶层组件块（fx-01）；图本体在 `GAS/graphs.json`（加载序先于 effects，引用许可序）。

## 4. 运行时加载效果

loader 校验槽组合（main/skipMain 互斥）与图 kind 要求；执行计划按四窗口组织编译（激活/周期/过期/移除），窗口内相位图即本块声明。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| main 与 skipMain 同相位同写 | 启动失败 |
| OnPropose 挂非 Validation 图 | 启动失败（fx-05） |
| 绑定步超上限 | 启动失败 |
| 监听器收集超容量 | 运行期报错 |

## 6. 实例

- `mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/GAS/effects.json`（OnPeriod.post）
- `mods/capabilities/navigation/MassNavigationMod/assets/GAS/effects.json`（OnApply.post + OnPeriod.post 双相位挂图）

**相关文档**：[fx-04 PRD](../prd/fx-05-phases.md) · [fx-05 配置说明](fx-06-proposal-window.md) · [gr-08](gr-08-mount-points.md)
