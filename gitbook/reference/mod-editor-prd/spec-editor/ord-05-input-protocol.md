# ord-05 editor spec · 输入协议

> 编辑器实现任务书。编辑器需求见 [ord-05 UXD](../uxd/ord-05-input-protocol.md)；引擎侧见 [runtime spec](../spec-runtime/ord-05-input-protocol.md)。

## 1. 概述
门等待视图实现：时间轴门节点、等待态快照、事件模拟注入。

## 2. 设计
- **门节点**：时间轴 item 的一种节点形态，标注等待标记（tag）与超时（payloadA）。
- **等待态**：会话期读门等待快照（等待标记/已等帧数/截止），三态着色。
- **模拟注入**：开发期调用事件注入接口伪造命中与超时；对运行会话有副作用，需显式开关。

## 3. 精确语义与不变量
- 步骤类型下拉只列引擎认识的类型，与加载器同源；未知类型保存期拦截。
- 等待态快照与引擎门状态一一对应，无编辑器侧推算。

## 4. 依赖接口与验收
- 消费：exec items 配置、门等待快照、事件注入接口。
- 验收：模拟命中后门放行与真实事件一致；写了未知步骤类型的能力保存被阻断。

**相关文档**：[ord-05 UXD](../uxd/ord-05-input-protocol.md) · [ord-05 runtime spec](../spec-runtime/ord-05-input-protocol.md)
