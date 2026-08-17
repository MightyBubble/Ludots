# attr-05 UXD · 属性绑定与 Sink 的编辑器需求

> attr-05 的编辑器需求（高保真规格）。第一性需求见 [attr-05 PRD](../prd/attr-05-bindings.md)；配置写法见 [attr-05 配置说明](../config/attr-05-bindings.md)；编辑器实现见 [editor spec](../spec-editor/attr-05-bindings.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

绑定面板是"属性→外部系统"的接线板：每条绑定声明属性、sink、通道与脉冲策略；并排展示 sink 通道占用全景。

## 2. 布局线框

```text
┌─ 绑定面板 ────────────────────────────────────────────────────────┐
├─ 左：绑定清单 ────────┬─ 右：绑定表单 ──────────────────────────────┤
│ ▸ Physics.X  ch0 脉冲 │ id [Bind.Physics.ForceInput2D.X]           │
│ ▸ Physics.Y  ch1 脉冲 │ attribute [Physics.ForceRequestX ▾]        │
│ ▸ Cam.MoveX  ch0 脉冲 │ sink [Physics.ForceInput2D ▾] ch [0]       │
│ ▸ ＋新建绑定          │ mode [Override▾] scale [1.0] reset [脉冲▾] │
├─ 底部：sink 通道全景 ──────────────────────────────────────────────┤
│ Physics.ForceInput2D [0● 1● ——]  Camera.BehaviorInput [0●…14● ——]  │
└────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 绑定清单 | 绑定表全量 | 按组折叠；脉冲徽标 |
| sink 下拉 | sink 注册表（启动冻结集） | 只列已注册 sink |
| 通道选择 | 选中 sink 的合法通道域 | 越界不可选 |
| 属性选择器 | 属性注册表枚举（attr-01 同源） | 只允许已注册名 |
| 通道全景 | 绑定表按 (sink,channel) 投影 | 占用冲突红显；零绑定 sink 灰显（死配置提示） |

## 4. 关键交互流：给相机加缩放通道绑定

1. 绑定面板 → ＋新建绑定；sink 选 `Camera.BehaviorInput`。
2. 通道选 8；属性选已注册的 `Camera.Behavior.Zoom`；mode/scale/reset 按需。
3. 保存 → 七字段全量校验通过 → 重启生效（绑定表结构变更）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 通道占用冲突 | 同 sink 同 channel 多条 | 全景红显、清单警示 |
| 零绑定 sink | 注册但无内容（现状 Graph.EdgeCostOverlay） | 灰显＋"死配置"提示 |
| 漏字段 | 表单不完整 | 保存禁用（无缺省兜底） |

## 6. 易用性验收口径

- 每条绑定的七字段在一张表单内一览，无隐藏缺省。
- sink 通道全景与运行时折叠分组一致，同帧结果可推演。

**相关文档**：[attr-05 PRD](../prd/attr-05-bindings.md) · [editor spec](../spec-editor/attr-05-bindings.md)
