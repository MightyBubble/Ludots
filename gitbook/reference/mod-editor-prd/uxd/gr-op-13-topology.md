# gr-op-13 UXD · 节点：拓扑谓词的编辑器需求

> gr-op-13 的编辑器需求（高保真规格）。第一性需求见 [gr-op-13 PRD](../prd/gr-op-13-topology.md)；配置写法见 [gr-op-13 配置说明](../config/gr-op-13-topology.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-13-topology.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

指挥与迷雾判定的三件小组件；重点是 KnowledgeHasProjection 与 LoadViewer 的"惯用搭档"引导。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：拓扑谓词 ──────────────────────────────────────┐
│ ▸ ControlDomainResolve（归属代表）                               │
│ ▸ ControlDomainControls（能指挥吗）                              │
│ ▸ KnowledgeHasProjection（观众知情吗 · 建议配 LoadViewer）        │
├─ 画布搭配建议条 ─────────────────────────────────────────────────┤
│  KnowledgeHasProjection.a 悬空 → 建议接 LoadViewer（E2）          │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 搭档建议 | 静态惯例映射 | a 悬空时推荐 LoadViewer |
| 谓词徽标 | 静态标注 | "纯读"标记 |
| kind 过滤 | 描述符掩码 | Query/Script 图置灰 |

## 4. 关键交互流：判定观众是否知情

1. Validation 图拖 KnowledgeHasProjection。
2. `a` 悬空点补全，首选 LoadViewer；`b` 接 LoadExplicitTarget。
3. Bool 输出接 JumpIfFalse 或 Validation 返回位。
4. 保存编译；调试面板显示观众-目标投影状态。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| a 未接观众 | a 接了非 Viewer 实体 | 信息条"任意观察者视角"说明 |
| 无效实体判定 | 输入可能无效 | 卡片注"无效返回假" |
| kind 不符 | 拓扑件拖入 Query/Script 图 | 置灰 |

## 6. 易用性验收口径

- KnowledgeHasProjection 从拖入到接好观众/目标 ≤ 3 步。
- 三件的纯读徽标在卡片首屏可见。

**相关文档**：[gr-op-13 PRD](../prd/gr-op-13-topology.md) · [editor spec](../spec-editor/gr-op-13-topology.md)
