# gr-op-10 配置说明 · 节点：效果与事件动作

> 配置写法与行为。第一性需求见 [gr-op-10 PRD](../prd/gr-op-10-effect-actions.md)；编辑器需求见 [UXD](../uxd/gr-op-10-effect-actions.md)；现状见 [reference](../reference/gr-op-10-effect-actions.md)。

## 1. 示例配置

节点画廊真实文件（`ApplyEffectTemplate.json`，显式目标上标记效果）：

```json
[
  {
    "id": "showcase.graph_op.ApplyEffectTemplate",
    "kind": "Effect",
    "entry": "explicit",
    "nodes": [
      { "id": "explicit", "op": "LoadExplicitTarget" },
      { "id": "apply", "op": "ApplyEffectTemplate", "effectTemplate": "Effect.GraphOpsAttr.Mark" }
    ],
    "controlEdges": [
      { "from": "explicit", "fromPort": "next", "to": "apply" }
    ],
    "valueEdges": [
      { "from": "explicit", "fromPort": "value", "to": "apply", "toPort": "target" }
    ]
  }
]
```

## 2. 逐 op 表

kind 全族为 E（Effect 专属）。imm 一律为符号。

| op | 输入引脚 | 输出 | 语义 |
|---|---|---|---|
| ApplyEffectTemplate | target a b + imm 模板 | — | 对 target 上模板效果；a/b→ForceX/Y CallerParams |
| FanOutApplyEffect | imm 模板 | — | 对管线列表逐个上模板效果 |
| ApplyEffectDynamic | target value | — | 按 value 的模板号上效果 |
| FanOutApplyEffectDynamic | value | — | 按 value 的模板号扇出上效果 |
| RemoveEffectTemplate | target + imm 模板 | — | 撤销目标身上的模板效果 |
| FanOutDispatchEffect | imm 派发预设 | — | 按预设把载荷效果派给列表（dst=预设） |
| FanOutDispatchEffectDynamic | value + dst 预设 | — | 按动态预设号派发 |
| ModifyAttributeAdd | target value + imm 属性 | — | 目标属性加 value（走提案聚合） |
| SendEvent | target value + imm 事件 tag | — | 对 target 发事件（值随事件走） |

互斥与陷阱：

- **a/b 是保留通道**：ApplyEffectTemplate 的 a/b 直通模板 CallerParams 的 ForceX/Y——不是通用参数口；要传业务参数走效果 configParams（fx-18），别挪用 a/b。
- Dynamic 与模板号的分工：号从哪来（LoadConfigEffectId、事件载荷、黑板）是图的事，节点只认 value 里的号。
- ModifyAttributeAdd 与 WriteSelfAttribute（gr-op-04）语义不同：前者对任意目标走提案聚合，后者直写自身 Current——治疗/伤害用前者，派生图回写用后者。
- 扇出件消费管线 TargetList：链上没有列表时编译期即发现（引脚不接列表）。
- 预算：单根 fan-out 上限见事实页；超限是运行失败不是截断。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`effectTemplate`/`dispatchPreset`/`eventTag`/`attribute` 写符号，见 gr-04。效果模板在 `assets/GAS/effects.json`（分片 `GAS/effects/`）；派发预设见 fx-15。

## 4. 运行时加载效果

符号在编译期对效果注册表/派发预设/事件 tag/属性注册表解析；执行期动作进效果事务，扇出受预算约束，RootId 继承调用方。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 符号未注册 | 编译失败，指明节点与符号 |
| 扇出超单根预算 | 运行失败并报计数（上限见事实页） |
| 事务失败 | 动作随事务回滚 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ApplyEffectTemplate.json`
- 同目录 `FanOutApplyEffect.json`、`ApplyEffectDynamic.json`、`FanOutApplyEffectDynamic.json`、`RemoveEffectTemplate.json`、`FanOutDispatchEffect.json`、`FanOutDispatchEffectDynamic.json`、`ModifyAttributeAdd.json`、`SendEvent.json`

**相关文档**：[gr-op-10 PRD](../prd/gr-op-10-effect-actions.md) · [fx-15 配置说明](fx-11-target-dispatch.md) · [gr-op-04 配置说明](gr-op-04-attributes.md)
