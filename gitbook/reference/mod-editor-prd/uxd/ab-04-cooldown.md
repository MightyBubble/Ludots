# ab-04 UXD · 冷却三件套的编辑器需求

> ab-04 的编辑器需求（高保真规格）。第一性需求见 [ab-04 PRD](../prd/ab-04-cooldown.md)；配置写法见 [ab-04 配置说明](../config/ab-04-cooldown.md)；编辑器实现见 [editor spec](../spec-editor/ab-04-cooldown.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

冷却向导把三件拼装为一个动作：输入时长，自动生成 TagClip 与 blockTags 条目；契约块与共享关系同屏可查。

## 2. 布局线框

```text
┌─ 冷却面板：ArcaneShift ──────────────────────────────────────────┐
├─ 向导 ────────────────────────────────────────────────────────────┤
│ 冷却时长 [72 tick ≈ 1.2s]  tag [Cooldown.Champion.Ezreal.E ▾]     │
│ ☑ 自动写入时间轴 TagClip (t0, dur 72)   ☑ 自动写入 blockTags      │
├─ 共享视图 ────────────────────────────────────────────────────────┤
│ Cooldown.Champion.Ezreal.E ← 仅本技能                              │
│ Cooldown.UtilityAutocast.GCD ← Attack · HealBurst · Curse (3)     │
├─ 底：契约块 [cooldown 未声明｜valueAttribute ▾ | tag ▾] ──────────┤
└────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 时长输入 | tick 数 + 当前时钟换算的秒数只读显示 | 改写即同步时间轴条目 |
| 冷却 tag 选择器 | tag 注册表（Cooldown.* 族） | 新名提示"首现即注册"；共享同 tag 的技能即时列出 |
| 向导开关 | TagClip/blockTags 联动写入 | 关闭任一则提示闭环缺口 |
| 共享视图 | 全技能冷却 tag 反向索引 | 点技能名跳转；共享组一键浏览 |
| 契约块表单 | cooldown 两字段 | 与实战闭环独立标注"仅查询用" |
| 预览环 | 定时 tag 剩余时间（试播态） | 图标转圈演示 |

## 4. 关键交互流：给新技能配 2 秒冷却

1. 冷却面板时长输入 120 tick（FixedFrame 换算 2.0s）。
2. tag 选择器选既有共享 tag 或新建 `Cooldown.<族>.<键位>`。
3. 确认 → 时间轴 t0 出现 TagClip、blockTags 出现该 tag（两处高亮闪示）。
4. 共享视图刷新成员列表。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 闭环缺口 | 有 TagClip 无 blockTags（或反向） | 黄条"冷却不生效：缺挡板/缺挂载" |
| 契约独活 | 只声明 cooldown 块 | 灰字标注"不挡施放，仅 AI/界面查询" |
| tag 撞族 | 冷却 tag 与状态 tag 同名规则混用 | 提示命名空间纪律 |

## 6. 易用性验收口径

- 配一个生效冷却 ≤ 2 次交互（填时长 + 选 tag）。
- 闭环缺口在编辑器先于测试局被发现。

**相关文档**：[ab-04 PRD](../prd/ab-04-cooldown.md) · [editor spec](../spec-editor/ab-04-cooldown.md)
