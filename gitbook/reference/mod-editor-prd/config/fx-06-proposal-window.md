# fx-06 配置说明 · 提案窗口与 Instant 内联

> 配置写法与行为。第一性需求见 [fx-06 PRD](../prd/fx-06-proposal-window.md)；编辑器需求见 [UXD](../uxd/fx-06-proposal-window.md)；现状见 [reference](../reference/fx-06-proposal-window.md)。

## 1. 示例配置

教学骨架（仓库暂无同构实例）：在模板上挂纯相位图——

```json
[
  { "id": "Effect.Example.Corrosive", "presetType": "DoT",
    "lifetime": "After", "participatesInResponse": true,
    "duration": { "durationTicks": 300, "periodTicks": 30, "clockId": "FixedFrame" },
    "phaseGraphs": {
      "OnPropose":   { "main": "Graph.Example.CorrosiveValidate" },
      "OnCalculate": { "main": "Graph.Example.CorrosivePotency" } } }
]
```

`Graph.Example.CorrosiveValidate` 为 Validation kind：默认输出"否"，条件全满足时显式写"是"。

## 2. 字段与行为

| 规则 | 这样配会产生什么效果 |
|---|---|
| `OnPropose` 挂图 | 必须 Validation kind；通过才进窗口 |
| `OnCalculate` 挂图 | Effect kind；纯计算，产数值供后续相位 |
| 纯相位监听图 | 只许纯图（非纯相位只许纯/事务图） |
| 空相位 | 未挂 OnPropose 图直接通过 |
| Instant 内联 | 模板 Instant 且无周期：同帧走完，不建实体（fx-04） |

外部原子独占律（激活窗口）——只有满足全部条件才合法：

- 模板寿命为 Instant；恰一个外部原子；零事务图；外部原子是窗口最后一步；不与 modifiers、grantedTags、监听器设置块组合。
- 现行外部原子仅三类：位移、进度、订单。视野揭示、兑换、关系修改、生命周期内建等一律 fail-closed 拒绝（域清单见 reference）。

## 3. 文件结构

提案窗口不引入新文件：验证/计算图在 `GAS/graphs.json`，经模板 phaseGraphs 挂接（fx-05）。

## 4. 运行时加载效果

执行计划编译期做 fail-closed 检查：纯相位非纯操作计数>0 即抛；监听图禁内建调用与配置读；窗口独占律违反抛组合违例。四窗口全编译完成模板才可用。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 纯相位图含副作用 / 监听图禁用 op | 编译期启动失败 |
| 外部原子组合违例 | 编译期抛组合违例 |
| 运行期监听器预检撞独占律 | 运行期拒绝该提案 |

## 6. 实例

- 内联消费：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json` 的全部 Instant 条目（无 phaseGraphs，走默认处理器）
- 纯相位验证图暂无仓库实装样例——示例缺口随 todo/effect.md E1 一并补样

**相关文档**：[fx-06 PRD](../prd/fx-06-proposal-window.md) · [fx-05 配置说明](fx-05-phases.md) · [gr-04](gr-04-compilation.md)
