# input-05 UXD · 过滤与输入方案的编辑器需求

> input-05 的编辑器需求（高保真规格）。第一性需求见 [input-05 PRD](../prd/input-05-filters-and-schemes.md)；配置写法见 [input-05 配置说明](../config/input-05-filters-and-schemes.md)；编辑器实现见 [editor spec](../spec-editor/input-05-filters-and-schemes.md)。

## 1. 界面定位

输入地基工作台：四页签——动作绑定表、过滤档案、控制方案、属性绑定；全部输入侧档案的单一入口。

## 2. 布局线框

```text
┌─ 输入地基工作台 ─────────────────────────────────────────────────────┐
├─ 页签 [动作绑定|过滤档案|控制方案 ●|属性绑定] ────────────────────────┤
│ 方案 scheme.default                              允许 [全允许 ▾]     │
│ 上下文集 [Default_Gameplay ✔] [Physics2D □] ＋                     │
│ 默认意图 [intent.command.default ▾]  默认派发 [dispatch.all… ▾]     │
│ ▸轴移动 [动作 Zoom ▾] [订单 moveTo ▾] 节流[6tick] 步长[100cm]       │
├─ 底部：动作覆盖检查 ────────────────────────────────────────────────┤
│ ⚠ Hotkey1-9 / PrimaryClick 未在 Default_Gameplay 绑定（O9）→ 修复  │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 动作绑定页 | default_input 动作+上下文+绑定 | 动作/上下文增删；绑定设备路径录制 |
| 过滤页 | filter_profiles；锚点/展开枚举、tag 总账 | 展开结果实时试算（本方受控实体数） |
| 方案页 | control_schemes（纯键位）；上下文补全源 | 白名单编辑（下单偏好在 command_prefs.json，不属方案页） |
| 属性绑定页 | action_attribute_bindings；动作与属性注册表 | 全字段表单，缺字段即不可保存 |
| 覆盖检查 | 上下文×动作交叉矩阵 | 空格高亮为缺口（含 O9 清单） |

## 4. 关键交互流：补齐默认玩法上下文的热键

1. 动作绑定页切到 `Default_Gameplay`。
2. 覆盖检查点 ⚠ 进入 Hotkey1-9 行 → "复制自 Physics2D_Playground"。
3. 检视录制的设备路径，按需改键。
4. 保存 → default_input 落盘；⚠ 消失。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 覆盖缺口 | 上下文缺动作绑定 | 矩阵空格 + 缺口清单 |
| 方案越权 | 切到白名单外方案 | 拒绝 + 白名单直达 |
| 绑定半成品 | 属性绑定缺字段 | 表单红条，保存禁用 |

## 6. 易用性验收口径

- 四页签任一字段改动到保存 ≤ 2 跳。
- 覆盖检查与启动后实际可触发性一致（同源矩阵）。

**相关文档**：[input-05 PRD](../prd/input-05-filters-and-schemes.md) · [editor spec](../spec-editor/input-05-filters-and-schemes.md)
