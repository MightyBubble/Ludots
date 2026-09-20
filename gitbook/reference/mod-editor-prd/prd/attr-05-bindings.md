# attr-05 · 属性绑定与 Sink

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/attr-05-bindings.md)；编辑器需求见 [UXD](../uxd/attr-05-bindings.md)；引擎实现见 [runtime spec](../spec-runtime/attr-05-bindings.md)；editor spec 见 [editor spec](../spec-editor/attr-05-bindings.md)；现状见 [reference](../reference/attr-05-bindings.md)。

## 1. 定位

属性绑定把属性数值单向送往外部系统：物理的力输入、相机的行为通道。被绑定的属性是"每帧指令"，不是持续状态。

## 2. 产品承诺

- **属性即输出**：一条绑定=一个属性映射到一个 sink 的一个通道；数值变化驱动外部系统消费。
- **脉冲语义**：ResetToZeroPerLogicFrame 策略下每逻辑帧消费后源属性归零——不写就是零。
- **全显式无缺省**：七字段逐条全显式；漏写、未知 sink、通道越界、非法数值一律启动失败并指明条目。
- **sink 集封闭**：sink 注册表启动注册后冻结；绑定只能引用已注册 sink。
- **顺序稳定**：绑定折叠成组按确定序应用，同帧结果可复现。

## 3. 运行行为

绑定表在配置链尾段加载（引用许可序最后一环）；运行期绑定系统在聚合重算后逐组应用；相机行为状态每帧清零重填。

## 4. 异常承诺

id 与外层不一致、未知 sink、通道超出 sink 合法域、mode/scale/resetPolicy 取非法值——启动失败；相机目标实体数量不唯一——运行失败。

**相关文档**：[attr-01](attr-01-definition.md) · [attr-03](attr-03-aggregation.md) · [ent-01](ent-01-templates.md)
