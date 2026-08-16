# ai-10 UXD · GOAP 与 HTN 规划的编辑器需求

> ai-10 的编辑器需求（高保真规格）。第一性需求见 [ai-10 PRD](../prd/ai-11-goap-htn.md)；配置写法见 [ai-10 配置说明](../config/ai-11-goap-htn.md)；编辑器实现见 [editor spec](../spec-editor/ai-11-goap-htn.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

规划面板是世界观的账本与沙盘：atoms 是位、投影是数据源、目标与动作是方程——编辑器要把"256 位世界"翻译成人能看的目标-手段网络。

## 2. 布局线框

```text
┌─ 规划面板 ────────────────────────────────────────────────────────────┐
├─ 左：资产树 ─────────┬─ 右：规划沙盘 ─────────────────────────────────┤
│ atoms (1)            │ 目标 Attack.Goap [策略 Goap · 分 1.0]           │
│ ├ HasEnemy           │ 手段网络：                                     │
│ projection (1)       │   [目标 HasEnemy=1]                            │
│ ├ HasEnemy.FromTarget│        ▲ Submit.Attack (cost1)                 │
│ utility (1)          │        │ Pre∅ Post∅ Order=attackTarget         │
│ goap_actions (1)     │ 世界位视图： □□□■□□…（256 位·HasEnemy=1）      │
│ goap_goals (1)       │ 投影： Attack.TargetEntity≠null → HasEnemy     │
│ htn_domain (空)      │ 计划预演： [Submit.Attack] 1 步 · 总代价 1      │
│ ＋新建（六表）       │                                               │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 资产树 | 六表合并视图 | 分组计数；htn_domain 显示四数组结构 |
| atom 选择器 | atoms 注册表 | 全表通用引用源 |
| 投影表单 | Op 五值 + 黑板键选择器（order_types+内建） | Int/Entity 两组键按 Op 互斥切换 |
| 手段网络 | goap_actions/goap_goals/htn_domain 关联图 | Pre/Post 位掩码点选 256 位条 |
| 位视图 | atom 槽位投影 | 点亮=1；hover 显示 atom 名 |
| 计划预演 | 本地调 dry 规划 | 输出步序与总代价；失败给不可满足位 |
| Order 表单 | OrderTypeRegistry + SubmitMode | OrderTagId 字段不渲染（显式拒） |

## 4. 关键交互流：给采集单位配"有矿就采"

1. atoms 新建 HasOre；projection 建 IntEquals IntKey=矿数 IntValue=1→HasOre。
2. goap_actions 建 Submit.Gather（Order=gather、Bindings EntityToTarget）。
3. utility 建 goal（策略 Goap，Bool 考量 HasOre）。
4. 计划预演确认 [Submit.Gather] 步序成立；保存。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 未声明 atom | 引用名不在 atoms | 红条 + 一键"去声明" |
| 键互斥破坏 | Entity op 配了 IntKey（或反之） | 表单互斥切换兜底 + 校验 |
| 计划不可满足 | dry 规划失败 | 高亮缺口位与缺失动作 |
| 空 htn_domain | 四数组全空 | 树节点灰显"未使用 HTN" |

## 6. 易用性验收口径

- 256 位世界状态有可视化呈现，atom 名与位一一对应。
- 目标-手段网络可从任一 atom 反查全部 Pre/Post 引用。
- 计划预演与运行期 PlanExecutor 产出一致。

**相关文档**：[ai-10 PRD](../prd/ai-11-goap-htn.md) · [editor spec](../spec-editor/ai-11-goap-htn.md)
