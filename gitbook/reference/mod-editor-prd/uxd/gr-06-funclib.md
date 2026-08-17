# gr-09 UXD · 函数库 FuncLib 的编辑器需求

> gr-08 的编辑器需求（高保真规格）。第一性需求见 [gr-08 PRD](../prd/gr-06-funclib.md)；配置写法见 [gr-08 配置说明](../config/gr-06-funclib.md)；编辑器实现见 [editor spec](../spec-editor/gr-06-funclib.md)。

## 1. 界面定位

函数库面板：给 Script 图起名入库、看纯度状态、在别的图里按名调用。

## 2. 布局线框

```text
┌─ 函数库 ────────────────────────────────────────────────────────────┐
│ ▸ demo.const.seven    ●pure   Graph.FuncLib.Demo.ConstSeven  被调×0 │
│ ▸ ability.slash       ●pure   Graph.Ability.Slash            被调×2 │
│ ▸ ability.bash        ●pure   Graph.Ability.Bash             被调×1 │
│ ── ＋把当前图入库（仅 Script · 入库即纯度校验）                      │
├─ 详情：调用方 2 · 图预览 · [打开图] [改挂 ActionLib] ────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 函数清单 | func_lib 目录 + 图注册表 | 名/图/kind/纯度四列 |
| 纯度徽标 | 纯度闭包校验结果（引擎同源算法） | 失败时红标并给出可达路径 |
| 被调计数 | 全图 functionName 引用扫描 | 点击列出调用方图 |
| 入库动作 | 当前图（必须 Script） | 名字撞 ActionLib 即时拒 |
| 调用插入 | 函数目录 | 画布插 InvokeFunc 节点带 functionName 补全 |

## 4. 关键交互流：把图变成函数并调用

1. 打开 Script 图 → 函数库面板"入库" → 命名（撞名提示）。
2. 纯度闭包即时校验：可达挂起则拒绝并高亮路径。
3. 在另一张 Script 图里插调用节点 → 选函数 → 保存，装载链解析为图 id。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 不纯 | 校验发现可达挂起/环 | 红标 + 路径详情 + 指往 ActionLib 的建议 |
| 引用悬空 | graph 未注册或 kind 不一致 | 行级红条 |
| 零调用 | 被调计数 0 | 灰色提示"未被使用" |

## 6. 易用性验收口径

- 纯度失败从发现到看见违规路径 ≤ 2 跳。
- 入库动作在编辑器内不可能产生装载失败（校验前置同源）。

**相关文档**：[gr-08 PRD](../prd/gr-06-funclib.md) · [editor spec](../spec-editor/gr-06-funclib.md) · [gr-09 UXD](gr-07-actionlib.md)
