# fx-21 UXD · 出生下单的编辑器需求

> fx-21 的编辑器需求（高保真规格）。第一性需求见 [fx-21 PRD](../prd/fx-22-submit-order.md)；配置写法见 [fx-21 配置说明](../config/fx-22-submit-order.md)；编辑器实现见 [editor spec](../spec-editor/fx-22-submit-order.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

SubmitOrderFromBlackboard 效果编辑页的出生下单表单：槽位、黑板五键绑定、订单类型与提交模式。

## 2. 布局线框

```text
┌─ 效果编辑页 · 出生下单 ────────────────────────────────────────┐
│ 槽位   source [Source ▾]        target [Target ▾]              │
│ 黑板   种类 [Rts.SpawnTarget.Kind ▾]   位置 [...Position ▾]    │
│        实体 [...Entity ▾]  HexQ [...HexQ ▾]  HexR [...HexR ▾]  │
│ 订单   点移动 [moveTo ▾]   实体 [castAbility ▾]  arg0 [1]       │
│ 提交   (·)Immediate  ( )Queued                                  │
│ ℹ 黑板无目标时本效果静默跳过                                     │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 槽位下拉 ×2 | Source/Target/TargetContext | 无 None 选项 |
| 黑板五键选择 | 黑板键注册表 | 五键全部必选 |
| 订单类型选择 ×2 | 订单类型表 key | 只列已注册 key |
| arg0 | 整数 | 实体订单参数 |
| 提交模式单选 | Immediate/Queued | 二选一 |

## 4. 关键交互流：出厂单位走向集结点

1. 造单位效果的 onSpawnEffect 链到本效果（见 fx-15）。
2. 槽位保持缺省：source=Source（工厂，黑板宿主）、target=Target（新单位）。
3. 黑板五键选 `Rts.SpawnTarget.*` 族；点移动订单选 `moveTo`。
4. 提交模式 Immediate；保存后在工厂设置集结点即可验收。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 五键任一悬空 | 黑板键被删 | 选择器标"未注册"并阻保存 |
| 订单类型悬空 | 订单表 key 被删 | 同上 |
| 运行期提交被拒 | 诊断回流 | 效果实例标注拒绝原因（与订单系统错误码同源） |

## 6. 易用性验收口径

- 五键与两类订单的"谁写入/谁消费"关系在表单内一跳可见。
- "黑板无目标=静默跳过"的语义一跳可见。

**相关文档**：[fx-21 PRD](../prd/fx-22-submit-order.md) · [editor spec](../spec-editor/fx-22-submit-order.md)
