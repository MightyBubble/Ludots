# gr-op-09 editor spec · 节点：聚合与迭代

> 编辑器实现任务书。编辑器需求见 [gr-op-09 UXD](../uxd/gr-op-09-aggregate.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-09-aggregate.md)。

## 1. 概述

收口件目录与有效位引导：双输出渲染、悬空检测、空集标注。

## 2. 设计

- **目录条目**：描述符表扫描三行；基准徽标与空集标注为编辑器静态映射。
- **双输出渲染**：TargetListGet 按 flags=BoolScratchFlags 渲染实体与有效位两个输出引脚。
- **悬空检测**：图保存前扫描有效位输出的消费状态，产出黄条诊断。
- **替代建议**：Query 图内 TargetListGet 置灰时，提示文案链到 gr-op-07 聚合件。

## 3. 精确语义与不变量

- 引脚渲染与描述符 flags 同源；有效位类型恒 Bool。
- 检测只提示不阻断（越界是运行语义不是图错误）。

## 4. 依赖接口与验收

- 消费：描述符表（flags）、图扫描接口。
- 验收：TargetListGet 双输出可见；有效位悬空必有黄条；Query 图内置灰并给替代建议。

**相关文档**：[gr-op-09 UXD](../uxd/gr-op-09-aggregate.md) · [gr-op-09 runtime spec](../spec-runtime/gr-op-09-aggregate.md)
