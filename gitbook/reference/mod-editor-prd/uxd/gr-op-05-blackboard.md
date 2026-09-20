# gr-op-05 UXD · 节点：黑板的编辑器需求

> gr-op-05 的编辑器需求（高保真规格）。第一性需求见 [gr-op-05 PRD](../prd/gr-op-05-blackboard.md)；配置写法见 [gr-op-05 配置说明](../config/gr-op-05-blackboard.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-05-blackboard.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

黑板键的图内入口：键选择器带"谁在读写"反查，是图与订单/AI 共享状态的会合面。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：黑板 ──────────────────────────────────────────┐
│ ▸ 读  ReadBlackboard Float / Int / Entity                        │
│ ▸ 写  WriteBlackboard Float / Int / Entity （仅 Effect 图）      │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ ReadBlackboardFloat ──────────────┐                           │
│ │ key [showcase.bb.power ▾] Float    │                           │
│ │ source ●───────────────────● Float │                           │
│ └────────────────────────────────────┘                          │
│  键反查：图×12 · 订单内置键 0 · AI 0（上限见事实页）               │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 键选择器 | ConfigKeyRegistry 投影 + 订单内置键表（ord-04） | 按值类型过滤；显示来源徽标 |
| 写节点置灰 | kind=非 Effect | 目录与画布均置灰并注明 |
| 键反查 | 全图扫描 + 订单内置键声明 | 点击跳全部读写处 |
| 用量条 | 黑板条目 vs 上限（事实页） | 预警 |

## 4. 关键交互流：AI 图读订单写好的目标

1. Score 图拖 ReadBlackboardEntity，键选择器输 `persistentStoredTarget` 前缀。
2. 选择器列出订单内置五键（ord-04），来源徽标"订单"；选中主目标键。
3. `source` 接 LoadContextSource；输出实体接 AggMinByDistance 之前的过滤链。
4. 保存编译通过；反查面板显示该键被订单系统与两张图共用。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 键未注册 | 手输键名不在注册表 | 红字 + 注册指引 |
| 类型不符 | Float 键选进 Int 节点 | 选择器过滤 + 手输标红 |
| 共享键 | 键被订单/AI/图多方用 | 徽标 + 反查列表 |

## 6. 易用性验收口径

- 任一键"定义+全部读写处"≤ 2 跳可达。
- 非 Effect 图里写节点从目录到画布都不可落下。

**相关文档**：[gr-op-05 PRD](../prd/gr-op-05-blackboard.md) · [editor spec](../spec-editor/gr-op-05-blackboard.md)
