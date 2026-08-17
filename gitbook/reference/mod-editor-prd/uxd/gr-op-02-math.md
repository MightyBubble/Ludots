# gr-op-02 UXD · 节点：数学与比较的编辑器需求

> gr-op-02 的编辑器需求（高保真规格）。第一性需求见 [gr-op-02 PRD](../prd/gr-op-02-math.md)；配置写法见 [gr-op-02 配置说明](../config/gr-op-02-math.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-02-math.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

节点面板"数学与比较"分组：公式编辑的核心件。双目节点是作者连线最多的形态。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：数学与比较 ────────────────────────────────────┐
│ ▸ 四则      AddFloat / MulFloat / SubFloat / DivFloat            │
│ ▸ 极值      MinFloat / MaxFloat                                  │
│ ▸ 变形      ClampFloat / AbsFloat / NegFloat / RandomFloat01     │
│ ▸ 比较      CompareGtFloat / CompareLtInt / CompareEqInt /       │
│             CompareEqEntity                                      │
│ ▸ 整数      AddInt                                               │
│ ▸ 选择      SelectEntity                                         │
├─ 画布节点卡 ─────────────────────────────────────────────────────┤
│        ┌ AddFloat ──┐          ┌ SelectEntity ─────────┐        │
│  a ●───┤            ├──● 值    │ condition ●           │        │
│  b ●───┤            │          │ a ● ────────────● 出  │        │
│        └────────────┘          │ b ●                   │        │
│                                 └───────────────────────┘        │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 节点目录条目 | 描述符表本族 op 行 | 按 kind 过滤；Query 图只剩 CompareEqEntity 可用 |
| 引脚类型徽标 | 描述符引脚列 | a/b 标 Float 或 Int 或 Entity，condition 标 Bool |
| 补全候选 | 值类型表 | Float 引脚只推 Float 输出源，混类型不出现 |
| 纯度提示 | RandomFloat01 标记 | 校验类图拖入时标"不可复现"警示 |

## 4. 关键交互流：写一个伤害公式

1. 拖 LoadAttribute（gr-op-04）读目标 Health 到画布。
2. AddFloat 的 `a` 引脚悬空点补全：选 ConstFloat，输 30。
3. 结果值线接 ModifyAttributeAdd 的 `value`（gr-op-10）。
4. 保存时类型全链检查通过，面板显示公式摘要 `Health + 30`。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 类型不符连线 | 拖线到类型不符引脚 | 连线弹回 + 红色引脚描边 |
| 引脚悬空 | 必需输入未连 | 节点卡黄条列出悬空引脚 |
| 随机节点进校验图 | RandomFloat01 入 Validation | 警示标"结果不可复现" |

## 6. 易用性验收口径

- 双目节点的 a/b 引脚从补全到连线成功 ≤ 2 次点击。
- 类型不符的连线在松手前即可见拒绝反馈，无需等保存。

**相关文档**：[gr-op-02 PRD](../prd/gr-op-02-math.md) · [editor spec](../spec-editor/gr-op-02-math.md)
