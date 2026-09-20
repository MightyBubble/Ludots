# ord-06 UXD · 输入映射的编辑器需求

> ord-06 的编辑器需求（高保真规格）。第一性需求见 [ord-06 PRD](../prd/ord-06-input-mappings.md)；配置写法见 [ord-06 配置说明](../config/ord-06-input-mappings.md)；编辑器实现见 [editor spec](../spec-editor/ord-06-input-mappings.md)。

## 1. 界面定位

输入映射编辑器：动作到订单的绑定总表——直连一条线，候选路由一张梯，参数与目标策略就地可改。

## 2. 布局线框

```text
┌─ 输入映射编辑器 ─────────────────────────────────────────────────────┐
├─ 顶：全局 [interactionMode ▾ AimCast]  覆写[✔ enabled] 路径[...] ────┤
├─ 左：映射清单 ────────┬─ 右：映射卡：SkillQ ──────────────────────────┤
│ ▸ SkillQ   技能 #0   │ 触发 [PressedThisFrame ▾]  路由(直连)         │
│ ▸ SkillW   技能 #1   │ orderTypeKey [castAbility ▾]  args i0 [0]     │
│ ▸ Command  候选路由 ● │ requireTarget [✘] targetType [Position ▾]     │
│ ▸ Stop     直连      │ castModeOverride [—继承全局▾]                 │
│ ▸ ＋新建映射         │ ▸候选路由梯（Command 卡：优先级+match 条件表）│
├─ 底部：试按键 ▶ [Q] → 预览：i0=0 castAbility → 演员×3 · 模式 AimCast─┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 映射清单 | 映射表 + 动作注册表 | 徽标区分直连/候选路由/技能 |
| 路由切换 | 二选一互斥 | 直连⇄候选切换时保留共同字段 |
| 候选梯 | candidates 数组 | 按优先级排序展示；match 条件行内编辑 |
| 参数槽与目标策略区 | argsTemplate i0-i3/f0-f3；targetType 枚举 + auto/cursor | 技能 i0 标"优先级"必填；互斥组联动禁用 |
| 试按键 | 动作模拟注入 + 路由干跑 | 展示"哪个演员会收到哪种单" |

## 4. 关键交互流：同一按键对兵营与坦克下不同单

1. 新建映射，动作选 `Command`，路由切"候选路由"。
2. 候选梯首行：`setSpawnTarget`，match 加 `abilityIdKeySuffix: ".Train"`。
3. 兜底行：`moveTo`，优先级 0，match 留空。
4. 试按键▶ 预览兵营→setSpawnTarget、坦克→moveTo；保存 → Input/input_order_mappings.json 落盘。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 动作悬空 | actionId 不在动作注册表 | 红条 + 跳转 default_input（input-05） |
| 路由冲突 | 直连与候选并存 | 保存阻断，二选一 |
| 候选永不命中 | 干跑显示有演员无单 | 黄条提示兜底候选 |
| 文件缺失 | mod 无映射文件 | 项目体检项（见 O7） |

## 6. 易用性验收口径

- 建一条直连映射 ≤ 3 次交互；候选路由加一层 ≤ 2 次。
- 试按键预览与实际运行逐演员一致（同源干跑）。

**相关文档**：[ord-06 PRD](../prd/ord-06-input-mappings.md) · [editor spec](../spec-editor/ord-06-input-mappings.md)
