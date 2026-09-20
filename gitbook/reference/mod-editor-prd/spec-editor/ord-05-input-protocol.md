# ord-05 editor spec · 输入协议

> 编辑器实现任务书。编辑器需求见 [ord-05 UXD](../uxd/ord-05-input-protocol.md)；引擎侧见 [runtime spec](../spec-runtime/ord-05-input-protocol.md)。

## 1. 概述
门等待视图实现：时间轴门节点、等待态快照、输入模拟注入。

## 2. 设计
- **门节点**：时间轴 item 的一种节点形态，payloadA 标注请求号/等待标记。
- **等待态**：会话期读门等待快照（等待号/已等帧数/截止），三态着色。
- **模拟注入**：开发期调用输入注入接口伪造命中与超时；对运行会话有副作用，需显式开关。

## 3. 精确语义与不变量
- InputGate 缺 payloadA 的判定与加载器同源（保存期拦截）。
- 等待态快照与引擎门状态一一对应，无编辑器侧推算。

## 4. 依赖接口与验收
- 消费：exec items 配置、门等待快照、输入注入接口。
- 验收：模拟命中后门放行与真实输入一致；缺 payloadA 的门保存被阻断。

**相关文档**：[ord-05 UXD](../uxd/ord-05-input-protocol.md) · [ord-05 runtime spec](../spec-runtime/ord-05-input-protocol.md)
