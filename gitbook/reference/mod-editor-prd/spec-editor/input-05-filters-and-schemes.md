# input-05 editor spec · 过滤与输入方案

> 编辑器实现任务书。编辑器需求见 [input-05 UXD](../uxd/input-05-filters-and-schemes.md)；引擎侧见 [runtime spec](../spec-runtime/input-05-filters-and-schemes.md)。

## 1. 概述
输入地基工作台实现：四页签共用一套注册视图，覆盖检查矩阵与设备路径录制。

## 2. 设计
- **动作绑定页**：写 `Input/default_input.json`；绑定编辑支持设备路径录制与组合键/处理器表单。
- **过滤页**：写 `Input/filter_profiles.json`；展开结果试算调用集合写入方同源接口。
- **方案页**：写 `Input/control_schemes.json`；三补全源为上下文/意图/派发注册表；白名单编辑器封闭集合语义。
- **属性绑定页**：写 `Input/action_attribute_bindings.json`；全字段表单与加载器同源必填校验。
- **覆盖矩阵**：上下文×动作交叉视图，空格即缺口；与实际触发面同源。

## 3. 精确语义与不变量
- 矩阵"已绑"判定与运行期动作可触发性一致。
- 过滤试算与引擎集合写入结果一致（同源）。

## 4. 依赖接口与验收
- 消费：default_input/过滤/方案/绑定四加载器、tag 与属性注册表、集合试算接口。
- 验收：补绑定保存后启动即可触发；白名单外方案切换被拒；缺字段绑定无法保存。

**相关文档**：[input-05 UXD](../uxd/input-05-filters-and-schemes.md) · [input-05 runtime spec](../spec-runtime/input-05-filters-and-schemes.md)
