# gr-06 UXD · 动作库 ActionLib 的编辑器需求

> gr-06 的编辑器需求（高保真规格）。第一性需求见 [gr-06 PRD](../prd/gr-07-actionlib.md)；配置写法见 [gr-06 配置说明](../config/gr-07-actionlib.md)；编辑器实现见 [editor spec](../spec-editor/gr-07-actionlib.md)。

## 1. 界面定位

动作库面板：按宿主分组管理动作，把"这张图能不能挂起"讲成一句人话。

## 2. 布局线框

```text
┌─ 动作库 ────────────────────────────────────────────────────────────┐
│ [全部 11] [BehaviorTree 5] [Hfsm 4] [Level 1] [Script 1]            │
│ ▸ bt.attack          ⏸可挂起  Graph.BT.Leaf.Attack      挂点:BT 叶  │
│ ▸ hfsm.combat.onTick ⛔不可挂  Graph.HFSM.Combat.OnTick 挂点:HFSM    │
│ ▸ script.drinkUntilFull ⏸可挂 Graph.Script.DrinkUntilFull 挂点:脚本 │
│ ── ＋把当前图入库（选宿主 · 即时政策校验）                           │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 宿主分组页签 | 动作目录 + 宿主枚举 | 计数实时 |
| 挂起徽标 | 宿主政策表 × 图可达挂起分析 | 可挂起/不可挂两态 + 违规详情 |
| 入库向导 | 当前图（必须 Script）+ 宿主四选一 | 选不可挂宿主即时校验可达 Yield |
| 撞名检查 | 函数+动作双目录 | 即时提示 |
| 挂点跳转 | 挂接点表（gr-07） | 按宿主列出可挂位置 |

## 4. 关键交互流：给 BT 挂一张可挂起动作

1. 打开 Script 图（含 Yield）→ 动作库"入库"。
2. 宿主选 BehaviorTree（政策允许挂起）→ 校验通过。
3. BT 编辑处按名引用动作（gr-07 BT 叶挂点）→ 装载零诊断。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 政策违规 | 选 Hfsm/Level 且图含可达 Yield | 红条 + Yield 路径 + 建议改宿主 |
| 撞名 | 与函数库重名 | 即时提示双出处 |
| 图已删 | graph 未注册 | 行级失效 |

## 6. 易用性验收口径

- 宿主选择时政策后果（可否挂起）一屏可见。
- 编辑器内不可能产出政策违规或撞名的入库条目。

**相关文档**：[gr-06 PRD](../prd/gr-07-actionlib.md) · [editor spec](../spec-editor/gr-07-actionlib.md) · [gr-07 UXD](gr-08-mount-points.md)
