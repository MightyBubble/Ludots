# gr-op-04 UXD · 节点：属性与配置的编辑器需求

> gr-op-04 的编辑器需求（高保真规格）。第一性需求见 [gr-op-04 PRD](../prd/gr-op-04-attributes.md)；配置写法见 [gr-op-04 配置说明](../config/gr-op-04-attributes.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-04-attributes.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

数值读写的符号选择面：属性选择器与配置键选择器；WriteSelfAttribute 单独承载"直写"警示。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：属性与配置 ────────────────────────────────────┐
│ ▸ 读        LoadAttribute / LoadSelfAttribute                    │
│ ▸ 写        WriteSelfAttribute ⚠直写                              │
│ ▸ 配置      LoadConfigFloat / LoadConfigInt / LoadConfigEffectId │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ WriteSelfAttribute ⚠ ────────────┐                            │
│ │ attribute [Health ▾]              │                            │
│ │ value ●（Float）                  │                            │
│ └───────────────────────────────────┘                            │
│  ⚠ 直写 Current，绕过修改器聚合（对比：修改器走 fx-05）            │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 属性选择器 | 属性注册表投影（上限见事实页） | 搜索 + 血条/普通徽标；未注册名标红 |
| 配置键选择器 | ConfigKeyRegistry 投影 | 只列已注册键；显示值类型 |
| 直写警示 | derivedWrite 标记 | WriteSelfAttribute 卡片常驻警示条 |
| 监听图拦截 | listenerOwner 标记 + 图宿主类型 | 监听图内 LoadConfig 置灰并注明原因 |

## 4. 关键交互流：Derived 图回写自身属性

1. Derived 图里拖 LoadSelfAttribute 选 `Health` 读当前值。
2. 接 AddFloat 加 10，结果接 WriteSelfAttribute 的 `value`，属性同样选 `Health`。
3. 警示条展开显示"直写 Current"说明；确认后保存。
4. 编译通过，属性面板引用计数 +1。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 属性未注册 | 名字手输不在注册表 | 红字 + attr-01 首现注册说明 |
| 直写冲突 | 同图同属性既有修改器又直写 | 黄条"两路写入并存"提示 |
| 监听图禁用 | LoadConfig 在监听宿主图 | 置灰 + "无 owner 模板上下文" |

## 6. 易用性验收口径

- 属性选择器输入到选中 ≤ 3 步；血条型属性带徽标可见。
- WriteSelfAttribute 的直写警示在卡片首屏可见，不需展开。

**相关文档**：[gr-op-04 PRD](../prd/gr-op-04-attributes.md) · [editor spec](../spec-editor/gr-op-04-attributes.md)
