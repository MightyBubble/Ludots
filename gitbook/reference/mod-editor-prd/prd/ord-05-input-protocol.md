# ord-05 · 输入协议

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ord-05-input-protocol.md)；编辑器需求见 [UXD](../uxd/ord-05-input-protocol.md)；引擎实现见 [runtime spec](../spec-runtime/ord-05-input-protocol.md)；editor spec 见 [editor spec](../spec-editor/ord-05-input-protocol.md)；现状见 [reference](../reference/ord-05-input-protocol.md)。

## 1. 定位

输入协议是能力执行时间轴与输入系统之间的问答应答：执行中的门发起等待并留下请求号，输入系统在后续帧应答，门按请求号配对消费——放行、或改写目标。

## 2. 产品承诺

- **一问一答配对**：等待请求与响应按请求号严格配对，串号的响应被忽略，不误配。
- **门上改写目标**：输入门的应答可以把执行实例的目标与目标上下文回填——目标在门后才定型。
- **事件门有期限**：等待事件标记的门，标记命中即放行；到期未命中也放行但不带数据。
- **响应有生产者**：确认类输入动作为固定的响应生产者，作者只声明门，不写应答逻辑。

## 3. 运行行为

能力执行进入门时构造请求入队并置等待态；此后每帧处理门：按等待请求号取响应，命中且目标存活则回填；输入门的请求号来自门声明，未声明即非法。

## 4. 异常承诺

输入门缺少请求号声明一律启动失败；应答目标已消亡不回填、不中断执行。

**相关文档**：[配置说明](../config/ord-05-input-protocol.md) · [ord-06](ord-06-input-mappings.md) · [ord-04](ord-04-blackboard.md)
