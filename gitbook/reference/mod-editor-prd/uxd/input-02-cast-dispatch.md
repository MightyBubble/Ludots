# input-02 UXD · 施法派发档案的编辑器需求

> input-02 的编辑器需求（高保真规格）。第一性需求见 [input-02 PRD](../prd/input-02-cast-dispatch.md)；配置写法见 [input-02 配置说明](../config/input-02-cast-dispatch.md)；编辑器实现见 [editor spec](../spec-editor/input-02-cast-dispatch.md)。

## 1. 界面定位

派发档案编辑器：选人策略 + 评分因素 + 路由形态三段式；配完用编队沙盘预演"谁出手、什么顺序"。

## 2. 布局线框

```text
┌─ 派发档案编辑器：dispatch.nearest_top_n ─────────────────────────────┐
├─ 三段卡 ─────────────────────────────────────────────────────────────┤
│ 选人 [topN ▾]  N [3]        （all / cycle+advanceOn▾ 联动显隐）      │
│ 评分 [utility ▾]  因素 [distanceToTarget:invert ×] ＋                 │
│ 路由 [parallel ▾]  共享单号 [✔]   （sequential 隐藏共享项）          │
├─ 编队沙盘 ───────────────────────────────────────────────────────────┤
│ 目标 ✖  ▶ 演员群 [T1 T2 T3 T4 T5]（按分着色）                        │
│ 派发预演 → 出手 [T2 T5 T1]（近者优先）· 共享单号 #77                │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 选人下拉 | selector 三 kind | kind 切换联动参数区（N/advanceOn） |
| 因素编辑器 | considerations 语法（因素:修饰） | 补全已知因素；非法项红条 |
| 路由组 | router 两 kind + sharedOrderId | sequential 下共享项禁用 |
| 编队沙盘 | 派发干跑 + 会话演员集合 | 预演出手序与共享单号；cycle 显示当前轮到谁 |

## 4. 关键交互流：近者优先取前三

1. 新建档案 `dispatch.nearest_top_n`。
2. 选人选 `topN`，N 填 3。
3. 因素加 `distanceToTarget:invert`。
4. 路由 parallel + 共享单号；沙盘▶ 预演出手序 [T2 T5 T1]。
5. 保存后在控制方案 defaults 引用（input-05）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| topN 缺 N | kind=topN 且 n 空 | 保存阻断 |
| 因素悬空 | 未知因素名 | 红条 + 已知因素清单 |
| 轮转不推进 | cycle 档案 + 运行观测 | 治理提示（O8）+ 链接 |

## 6. 易用性验收口径

- 建一个三段完整档案 ≤ 5 次交互。
- 沙盘预演与运行期出手序一致（同源干跑）。

**相关文档**：[input-02 PRD](../prd/input-02-cast-dispatch.md) · [editor spec](../spec-editor/input-02-cast-dispatch.md)
