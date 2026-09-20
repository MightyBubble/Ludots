# ab-02 UXD · 执行时间轴的编辑器需求

> ab-02 的编辑器需求（高保真规格）。第一性需求见 [ab-02 PRD](../prd/ab-02-exec-timeline.md)；配置写法见 [ab-02 配置说明](../config/ab-02-exec-timeline.md)；编辑器实现见 [editor spec](../spec-editor/ab-02-exec-timeline.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

时间轴编辑器是技能的剪辑台：一条 tick 轨道，拖入四类条目，即时看到时长、顺序与打断点。

## 2. 布局线框

```text
┌─ 时间轴编辑器：BuildPowerPlant ──────────────────────────────────┐
├─ 顶：时钟 [FixedFrame ▾]  打断 tags [Status.Stunned ×]  长度 120t ┤
├─ 轨道区 ──────────────────────────────────────────────────────────┤
│ 0        15       30        60        90       105      120 (tick)│
│ [TagClip ═══════════════════════ Status.Building.PP ═══]          │
│ [E0]…[E7] EffectSignal ▸ CostStep   [S]TagSignal [■]End          │
├─ 左：条目调色板 Clip/Signal/Gate/End · ＋拖入轨道 ────────────────┤
│ 右：选中条目  kind TagClip  tick 0  duration 120  tag [Status…▾]  │
├─ 底：占位 [10/16]  校验 [✔]  ▶时间轴试播 ─────────────────────────┤
└────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 时钟选择器 | 三种时钟域（rt-01） | 整轴基准；条目可单独覆盖 |
| 轨道区 | items 数组按 tick 投影 | 拖动改 tick；Clip 长条表 duration |
| 条目调色板 | 11 种 kind（四组分栏） | 拖入轨道即生成带必填字段的骨架 |
| tag/template 选择器 | tag 注册表、效果模板注册表 | 只列已注册名；双击跳对应编辑器 |
| payloadA 编辑器 | 按 kind 切换语义（加/删、请求 id、超时 tick） | 语义化控件，不暴露裸整数 |
| 占位计数 | items 数 vs 16 上限 | 接近上限预警 |

## 4. 关键交互流：把瞬发伤害改成 0.5 秒后生效

1. 轨道选中 `EffectSignal` 条目。
2. 属性面板 tick 从 0 改 30（FixedFrame 60fps 下 0.5 秒），条目即时右移。
3. 保存 → 分片落盘，校验全绿。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 轨道满 | items 达 16 | 调色板拖入禁用 |
| Gate 无超时 | EventGate payloadA=0 | 条目标"无限等待"徽标 |
| tag 未注册 / tick 乱序 | tag 新名 / 条目 tick 小于前条 | 黄标"首现即注册" / 警示（消费按数组序，不重排） |

## 6. 易用性验收口径

- 四类条目任意一种的添加 ≤ 2 次交互。
- 时间轴试播与引擎推进同源（同 tick 序、同终态）。

**相关文档**：[ab-02 PRD](../prd/ab-02-exec-timeline.md) · [editor spec](../spec-editor/ab-02-exec-timeline.md)
