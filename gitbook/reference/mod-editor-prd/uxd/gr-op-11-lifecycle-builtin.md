# gr-op-11 UXD · 节点：生命周期与内建的编辑器需求

> gr-op-11 的编辑器需求（高保真规格）。第一性需求见 [gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md)；配置写法见 [gr-op-11 配置说明](../config/gr-op-11-lifecycle-builtin.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-11-lifecycle-builtin.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

生命周期图的组装台：两颗节点配一张二十行的内建菜单；默认链模板让作者从"复制引擎默认部署链"起步。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：生命周期与内建（仅 Effect 图）──────────────────┐
│ ▸ BeginLifecycleTransaction（事务开关）                           │
│ ▸ InvokeBuiltin ▾ 内建菜单（20 项分组同配置说明）                 │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ InvokeBuiltin ───────────────────────┐                        │
│ │ handler [MaterializeTemplate ▾]       │                        │
│ │ 读参：ProjectileParams/UnitCreation…  │（随 handler 变）       │
│ └───────────────────────────────────────┘                        │
│  模板链：[部署吞噬链] [空链]   ← 一键插入七节点默认链              │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 内建菜单 | 内建注册表 20 handler（分组） | 选中即写 `builtinHandler` |
| 读参说明 | handler→参数块静态映射 | 卡片显示该内建读哪些效果参数 |
| 模板链按钮 | 引擎默认图（DeployConsumeSource） | 一键插入七节点链 |
| 组合门警示 | Lifecycle fail-closed 元数据 | 折叠视图内标红 |

## 4. 关键交互流：定制一条部署链

1. Effect 图拖 BeginLifecycleTransaction；点模板链按钮插入默认七节点。
2. 删掉 ClearActiveEffects，保留身份复制与吞噬。
3. 内建菜单把 ConsumeEntity 换成 MaterializeTemplate 调序——控制线拖动重排。
4. 保存编译；折叠进效果模板的尝试被组合门红条拦下。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 事务缺失 | InvokeBuiltin 不在事务后 | 红条"内建需生命周期事务" |
| 组合门拦截 | 折叠视图含本族 | 红条 Lifecycle 域 |
| handler 失效 | 注册表无此名（mod 卸载后） | 节点红框 + 符号保留 |

## 6. 易用性验收口径

- 内建菜单按分组 ≤ 2 滚屏可见全部 20 项。
- 默认链插入后直接可编译。

**相关文档**：[gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md) · [editor spec](../spec-editor/gr-op-11-lifecycle-builtin.md)
