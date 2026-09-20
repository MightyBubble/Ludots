# gr-op-06 UXD · 节点：空间查询的编辑器需求

> gr-op-06 的编辑器需求（高保真规格）。第一性需求见 [gr-op-06 PRD](../prd/gr-op-06-spatial.md)；配置写法见 [gr-op-06 配置说明](../config/gr-op-06-spatial.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-06-spatial.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

目标选择的形状工具箱：形状节点 + 管线链。容量策略与中心规则是两处最易错的设定，都要可视化。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：空间查询 ──────────────────────────────────────┐
│ ▸ 形状  QueryRadius / Cone / Rectangle / Line / Hex×3            │
│ ▸ 管线  QuerySortStable / QueryLimit / FilterNotEntity /         │
│         FilterLayer / FilterRelationship                         │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ QueryCone ────────────────────────────┐                       │
│ │ 容量 [RequireComplete ▾]   中心=施法者 │                       │
│ │ a ●（朝向°）  b ●（半角°）   list ●── │→ 管线                │
│ └───────────────────────────────────────┘                       │
│  画布叠层：形状预览罩（锥形扇面随 a/b 变化）                        │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 容量策略下拉 | RequireComplete / AllowTruncated | 切换即写 `queryCapacityPolicy` |
| 形状参数框 | `radiusCm`/`rangeCm`/`hexRadius`/`layerMask`/`intValue` | 数值输入；层掩码出位开关组 |
| 中心徽标 | 中心规则静态表 | "中心=施法者"或"中心=目标点→施法者兜底" |
| 形状预览罩 | 地图渲染 + 形状参数 | 线性图画布上叠层预览命中范围 |
| dropped 计数 | 运行时诊断 | AllowTruncated 截断数在调试面板可见 |

## 4. 关键交互流：圈最近三个敌人

1. 拖 QueryRadius 设 `radiusCm=800`、策略 RequireComplete。
2. `list` 出引脚接 QuerySortStable，再接 QueryLimit 设 `intValue=3`。
3. 链尾接 TargetListGet（gr-op-09）取实体；预览罩随参数实时变化。
4. 保存编译；若 Query 图内拖锥形节点，面板提前置灰提示 kind 不符。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 管线断链 | list 引脚接了非 TargetList 源 | 连线弹回 |
| 截断发生 | AllowTruncated 且 dropped>0 | 调试面板黄条 |
| 容量失败 | RequireComplete 超容 | 运行诊断红条 + 容量上限（事实页） |
| kind 不符 | 锥形族拖入 Query 图 | 目录置灰 + 画布拒绝落点 |

## 6. 易用性验收口径

- 形状节点的容量策略与中心语义在卡片首屏可见。
- 三节点管线（形状→排序→截断）从空画布到编译通过 ≤ 5 步。

**相关文档**：[gr-op-06 PRD](../prd/gr-op-06-spatial.md) · [editor spec](../spec-editor/gr-op-06-spatial.md)
