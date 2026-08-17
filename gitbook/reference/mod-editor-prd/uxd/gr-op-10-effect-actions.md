# gr-op-10 UXD · 节点：效果与事件动作的编辑器需求

> gr-op-10 的编辑器需求（高保真规格）。第一性需求见 [gr-op-10 PRD](../prd/gr-op-10-effect-actions.md)；配置写法见 [gr-op-10 配置说明](../config/gr-op-10-effect-actions.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-10-effect-actions.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

Effect 图的动作出口：模板/预设/事件选择器三个符号面，加上"a/b 保留通道"的高压警示。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：效果与事件动作（仅 Effect 图）──────────────────┐
│ ▸ 上效果  ApplyEffectTemplate / FanOut… / …Dynamic ×2            │
│ ▸ 派发    FanOutDispatchEffect / …Dynamic                        │
│ ▸ 撤效果  RemoveEffectTemplate                                   │
│ ▸ 直改    ModifyAttributeAdd                                     │
│ ▸ 事件    SendEvent                                             │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ ApplyEffectTemplate ─────────────────┐                        │
│ │ 模板 [Effect.GraphOpsAttr.Mark ▾]     │                        │
│ │ target ●   a ⚠ ●  b ⚠ ●              │                        │
│ │ ⚠ a/b=ForceX/Y CallerParams 保留通道  │                        │
│ └───────────────────────────────────────┘                        │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 模板选择器 | 效果注册表投影（分片 `GAS/effects/`） | 搜索；显示 presetType 徽标 |
| 预设选择器 | 派发预设注册表（fx-15） | dst 目的位联动 |
| 事件 tag 选择器 | 事件 tag 注册表 | 只列已注册 tag |
| a/b 警示 | 引脚角色静态标注 | 连线时弹保留通道说明 |
| 预算投影 | 单根 fan-out 上限（事实页） | 扇出节点显示链上预计目标数 vs 上限 |

## 4. 关键交互流：范围内群体减速

1. 空间链（gr-op-06）圈出列表，`list` 接 FanOutApplyEffect。
2. 模板选择器搜 `Slow` 选 `Effect.Example.Slow`；链式摘要显示预计扇出数。
3. 预算条低于上限（事实页数值），保存编译。
4. 调试运行：每个命中单位挂上减速，事务回滚演练走工作台回滚。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| a/b 误用 | a/b 接了非力参数源 | 连线弹窗说明保留通道 |
| 预算接近上限 | 链上预计数接近 fan-out 上限 | 黄条预警 |
| 符号缺失 | 模板/预设/事件未注册 | 红字 + 对应卷链接 |

## 6. 易用性验收口径

- 模板选择器输入到选中 ≤ 3 步且显示 presetType。
- 任何 a/b 连线动作伴随一次保留通道说明（可关不再扰）。

**相关文档**：[gr-op-10 PRD](../prd/gr-op-10-effect-actions.md) · [editor spec](../spec-editor/gr-op-10-effect-actions.md)
