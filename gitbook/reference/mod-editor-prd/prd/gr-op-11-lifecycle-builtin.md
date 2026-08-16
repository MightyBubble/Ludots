# gr-op-11 · 节点：生命周期与内建

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-11-lifecycle-builtin.md)；编辑器需求见 [UXD](../uxd/gr-op-11-lifecycle-builtin.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-11-lifecycle-builtin.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-11-lifecycle-builtin.md)；现状见 [reference](../reference/gr-op-11-lifecycle-builtin.md)。

## 1. 定位

实体生灭的图面：BeginLifecycleTransaction 开一笔生命周期事务，InvokeBuiltin 按 handler 名调用二十个 C# 内建处理器——模板物化、身份复制、属性切片、清效果、稳定 id 移交、吞噬实体，以及力/弹道/造单位/揭示/兑换/进度/下单等重活。

## 2. 产品承诺

- **事务开关**：BeginLifecycleTransaction 显式开事务，之后的内建调用同生共死；事务语义由生命周期管线定义。
- **内建按名调用**：InvokeBuiltin 的 imm 是 handler 符号，二十个内建各管一段确定性业务，参数从效果上下文读。
- **组合自由**：内建次序由控制流决定——引擎默认的部署吞噬链就是一张七节点图。
- **组合的门**：效果组合编译对生命周期域 fail-closed——这族只属于显式 Effect 图创作。

## 3. 运行行为

事务开启后内建逐个执行，均落实体变更；链尾事务终结。内建执行参数来自合并的效果参数（ModifierParams、ProjectileParams 等），不在图上重传。

## 4. 异常承诺

handler 符号未注册——编译失败并指明节点与符号。效果组合折叠遇到本族——编译拒绝（Lifecycle 域 fail-closed）。事务外调用内建——按生命周期管线校验拒绝。

**相关文档**：[配置说明](../config/gr-op-11-lifecycle-builtin.md) · [fx-23](fx-23-lifecycle-atomic.md) · [gr-op-10](gr-op-10-effect-actions.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
