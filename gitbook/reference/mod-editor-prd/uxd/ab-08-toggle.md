# ab-08 UXD · Toggle 技能的编辑器需求

> ab-08 的编辑器需求（高保真规格）。第一性需求见 [ab-08 PRD](../prd/ab-08-toggle.md)；配置写法见 [ab-08 配置说明](../config/ab-08-toggle.md)；编辑器实现见 [editor spec](../spec-editor/ab-08-toggle.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

Toggle 面板是开关技能的两态工作台：开态（tag+光环）与关态（收尾时间轴）并排编辑，状态回路一眼看全。

## 2. 布局线框

```text
┌─ Toggle 面板：Garen.Courage ─────────────────────────────────────┐
├─ 开态 ──────────────────────┬─ 关态 ──────────────────────────────┤
│ toggleTag [State.Garen.Courage ▾]│ deactivateExec 时间轴 (t0 End) │
│ activeEffects (≤4)               │ ☐ 瞬时完成（不声明时间轴）      │
│  [CourageAura ● 自身·无限] ＋    │                                 │
│ 回路图：[W]─播→挂tag─路由/规则─再按→摘tag→收尾 ────────────────────┤
│ ⚠ 提示：光环效果需带 toggleTag 身份（回收依赖生命周期）             │
└────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| toggleTag 选择器 | tag 注册表（State.* 族） | 显示被哪些路由/规则引用 |
| activeEffects 列表 | 效果模板注册表，≤4 | 每项显示"自身·无限"徽标；无身份 tag 的效果黄标 |
| 关断时间轴 | 复用时间轴编辑器（ab-02） | 只允许非 toggle 条目 |
| 回路图 | toggleSpec + 路由表 + 规则表交叉 | 点节点跳对应编辑器 |
| 状态预览 | 沙盒（同 ab-05/06/07 共享） | 开/关两帧演示 tag 与效果进出 |

## 4. 关键交互流：给英雄配姿态开关

1. 激活时间轴（ab-02）放冷却 TagClip + End。
2. Toggle 面板选 toggleTag（或新建 State.*）。
3. activeEffects 加光环效果；黄标提示补身份 tag → 点击跳效果编辑器加 grantedTag。
4. 关态勾"瞬时完成"；保存后回路图完整呈现。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 回收缺口 | activeEffects 效果不带 toggleTag 身份 | 黄条"关闭后效果无法回收" |
| 开关同 tag 冲突 | 多个 toggle 技能共用 toggleTag | 红条（开一个即互斥语义，需显式确认） |
| 槽满 | activeEffects 达 4 | "＋"禁用 |
| 无关断演出 | 未声明 deactivateExec | 显示"瞬时完成"灰字 |

## 6. 易用性验收口径

- 从零配一个可开关的 toggle ≤ 5 次交互。
- 回收缺口在编辑器先于测试局被发现。

**相关文档**：[ab-08 PRD](../prd/ab-08-toggle.md) · [editor spec](../spec-editor/ab-08-toggle.md)
