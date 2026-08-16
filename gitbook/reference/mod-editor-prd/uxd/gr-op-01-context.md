# gr-op-01 UXD · 节点：常量与上下文的编辑器需求

> gr-op-01 的编辑器需求（高保真规格）。第一性需求见 [gr-op-01 PRD](../prd/gr-op-01-context.md)；配置写法见 [gr-op-01 配置说明](../config/gr-op-01-context.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-01-context.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

图编辑器节点面板的"常量与上下文"分组：图的值入口。新手拖第一颗节点大概率落在这里。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：常量与上下文 ─────────────────────────────────┐
│ 🔍 搜索…                                                        │
│ ▸ 常量      ConstBool / ConstInt / ConstFloat                   │
│ ▸ 实体      LoadCaster / LoadExplicitTarget / LoadViewer        │
│ ▸ 上下文    LoadContextSource / LoadContextTarget / …Context    │
│ ▸ 环境      LoadEventPayloadInt / LoadEventPayloadFloat /       │
│             LoadTargetPosX / LoadTargetPosY                     │
├─ 画布节点卡 ────────────────────────────────────────────────────┤
│ ┌ ConstFloat ─────────────┐   ┌ LoadCaster ───────────┐        │
│ │ 值 [42.0]               │●  │ (无输入引脚)      E0 ● │        │
│ └──────────────────── ●───┘   └───────────────────────┘        │
│   当前 kind：Effect · 分组内 13 条全部可用                       │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 节点目录条目 | 描述符表本族 op 行 | 按当前图 kind 过滤，不可用条目置灰并注明缺哪种 kind |
| 常量字面量框 | ConstInt/ConstFloat/ConstBool 节点字段 | 数值/布尔输入，改动即写节点 JSON |
| `pinRegister` 选择 | 寄存器文件容量（事实页） | 仅 ConstInt 暴露；保留槽灰显不可选 |
| 载荷槽位选择 | imm 槽位范围（0..1 / 0..3） | 下拉，越界值标红拒绝 |

## 4. 关键交互流：给加法节点供一个常量

1. 画布上 AddFloat 节点的 `a` 引脚悬空，点引脚出补全菜单。
2. 补全只推荐类型匹配且 kind 可用的源：本族推荐 ConstFloat（Float 引脚）。
3. 拖入即建 ConstFloat 节点并自动连线；焦点落在字面量框。
4. 改值为 42，类型校验通过，边保持连接。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| kind 不可用 | 当前图 kind 不在 op 掩码内 | 目录条目置灰 + 原因提示 |
| 保留槽冲突 | `pinRegister` 指向 E0/E1/E2 或已占槽 | 红条，保存前必须改 |
| 上下文缺实体 | 预检发现挂接点不注入该上下文 | 节点卡标"此挂接点无此上下文" |

## 6. 易用性验收口径

- 本族任何节点从面板到画布连线成功 ≤ 2 次拖放。
- 悬空数值引脚的补全菜单里，类型不符的候选一律不出现。

**相关文档**：[gr-op-01 PRD](../prd/gr-op-01-context.md) · [editor spec](../spec-editor/gr-op-01-context.md)
