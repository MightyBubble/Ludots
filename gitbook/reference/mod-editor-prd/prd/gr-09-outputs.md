# gr-09 · Query 图输出

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-09-outputs.md)；编辑器需求见 [UXD](../uxd/gr-09-outputs.md)；引擎实现见 [runtime spec](../spec-runtime/gr-09-outputs.md)；editor spec 见 [editor spec](../spec-editor/gr-09-outputs.md)；现状见 [reference](../reference/gr-09-outputs.md)。

## 1. 定位

Query 图的输出合同：图跑完的 TargetList 与标量，按 outputs 声明物化成实体集合或摘要键值，供其他系统按名消费。

## 2. 产品承诺

- **声明即 schema**：每个输出声明类型与去向；编译期校验类型匹配与必填项，不进运行期才报。
- **两种去向封闭**：实体集合（TargetList 落实体集合描述符）与摘要标量（布尔/整数/浮点/实体按键写入）。
- **写回有主**：物化必须知道 owner 与 caster；帧内绑定目标上下文。
- **槽位有生命周期**：输出值进槽池带世代与退休队列，宿主实体销毁即排队清理。

## 3. 运行行为

图执行收尾时按 schema 写回；输出值存储容量来自运行时容量表（事实页）；清理系统订阅实体销毁。

## 4. 异常承诺

非 Query 图声明 outputs、schema 校验失败、owner 或 caster 为空——装载或执行失败；去向与类型不匹配在编译期拒绝。

**相关文档**：[配置说明](../config/gr-09-outputs.md) · [gr-03](gr-03-kinds.md) · [gr-08](gr-08-mount-points.md)
