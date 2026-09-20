# gr-op-09 UXD · 节点：聚合与迭代的编辑器需求

> gr-op-09 的编辑器需求（高保真规格）。第一性需求见 [gr-op-09 PRD](../prd/gr-op-09-aggregate.md)；配置写法见 [gr-op-09 配置说明](../config/gr-op-09-aggregate.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-09-aggregate.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

查询链的收口件：三件小节点，重点在"空表/越界怎么办"的下游引导。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：聚合与迭代 ────────────────────────────────────┐
│ ▸ AggCount        （列表→Int）                                   │
│ ▸ AggMinByDistance（列表→Entity，基准=击落点）                    │
│ ▸ TargetListGet   （下标→Entity + 有效位）                        │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ TargetListGet ──────────────────────┐                         │
│ │ value ●（Int 下标）   ● Entity  ● ✔ │                         │
│ └──────────────────────────────────────┘                         │
│  ✔ = 有效位输出；未消费时卡片黄条提示                                │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 基准徽标 | AggMinByDistance 静态标注 | 卡片标"距击落点" |
| 有效位输出 | 描述符 BoolScratchFlags | TargetListGet 双输出引脚渲染 |
| 未消费检测 | 图扫描 | 有效位悬空时黄条 |
| 空表提示 | 上游链可能为空 | 聚合卡标空集语义 |

## 4. 关键交互流：取最近敌人施法

1. 空间链尾 `list` 悬空点补全，推荐 AggMinByDistance（收口段）。
2. 输出实体接 ApplyEffectTemplate 的 `target`（gr-op-10）。
3. 空表路径：HasTag 式判空不可用（实体无 tag），编辑器提示接 SelectEntity 兜底或 JumpIfFalse 有效位门。
4. 保存编译，黄条清零。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 有效位悬空 | TargetListGet 的 ✔ 输出未连 | 黄条"越界静默"提示 |
| 无效句柄下游 | 输出直接进动作节点 | 提示接判空门 |
| Query 图限制 | TargetListGet 拖入 Query 图 | 置灰 + 替代建议（gr-op-07） |

## 6. 易用性验收口径

- 越界风险图（含 TargetListGet）保存前必见一次有效位提示。
- 空表语义在聚合卡片首屏可见。

**相关文档**：[gr-op-09 PRD](../prd/gr-op-09-aggregate.md) · [editor spec](../spec-editor/gr-op-09-aggregate.md)
