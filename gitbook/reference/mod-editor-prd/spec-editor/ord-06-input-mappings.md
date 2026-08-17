# ord-06 editor spec · 输入映射

> 编辑器实现任务书。编辑器需求见 [ord-06 UXD](../uxd/ord-06-input-mappings.md)；引擎侧见 [runtime spec](../spec-runtime/ord-06-input-mappings.md)。

## 1. 概述
映射编辑器实现：映射卡、候选梯、目标策略联动、试按键干跑。

## 2. 设计
- **映射卡**：写 `Input/input_order_mappings.json`；路由二选一互斥在视图模型层强制。
- **候选梯**：candidates 排序视图，match 条件行内编辑，重复条件即时去重提示。
- **试按键**：调用路由干跑接口（无副作用），输出"演员→订单类型"清单；与引擎择单同源。
- **动作对账**：actionId 与 default_input 动作注册表交叉索引，悬空即红条。

## 3. 精确语义与不变量
- 卡片可产生的映射形状 = 加载器接受的形状（同源校验）。
- 干跑结果与运行期逐演员择单逐字一致。

## 4. 依赖接口与验收
- 消费：映射表加载器、动作注册表、实体集合键、路由干跑接口。
- 验收：候选路由保存后启动即生效；试按键预览与实测一致；悬空动作在保存前拦截。

**相关文档**：[ord-06 UXD](../uxd/ord-06-input-mappings.md) · [ord-06 runtime spec](../spec-runtime/ord-06-input-mappings.md)
