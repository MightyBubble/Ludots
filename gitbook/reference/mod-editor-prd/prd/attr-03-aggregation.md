# attr-03 · 聚合管线

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/attr-03-aggregation.md)；编辑器需求见 [UXD](../uxd/attr-03-aggregation.md)；引擎实现见 [runtime spec](../spec-runtime/attr-03-aggregation.md)；editor spec 见 [editor spec](../spec-editor/attr-03-aggregation.md)；现状见 [reference](../reference/attr-03-aggregation.md)。

## 1. 定位

聚合管线把 Buff 类数值从"记账"改为"重算"：每帧对打脏实体重算当前值——Base 复位、叠加全部活跃聚合效果、再跑派生图。效果过期自动退出叠加，无需反向补偿。

## 2. 产品承诺

- **重算即真相**：标了聚合脏的实体，Current 由 Base 与全部活跃聚合效果按存活序重算；Buff 走了数值自己回去。
- **双轨不串**：直改的 Current 是持久值，重算后保留；聚合写只动上限 Cap，聚合效果消退后上限回落、持久值仍在。
- **资格可查**：效果是否参与聚合由预设类型唯一决定，编辑器与文档可查（现状仅 Buff 预设，见 config）。
- **派生垫后**：派生属性图在聚合完成后执行，读到的是本帧聚合结果；被派生接管的属性当帧纯重算。
- **失败透明**：重算遇到缺组件、缺图程序——失败并指明实体与缺失项，不静默跳过。

## 3. 运行行为

聚合重算在属性绑定与相机系统之前执行；逐属性与重算前值比对，有变化才打脏、发表现位；聚合脏标记一次性，消费即移除。

## 4. 异常承诺

实体缺脏组件进重算即错；派生绑定缺图程序启动失败；效果处于未提交或取消态不参与叠加——属语义不是异常。

**相关文档**：[attr-02](attr-02-modifiers.md) · [attr-04](attr-04-derived.md) · [attr-06](attr-06-events.md)
