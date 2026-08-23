# ab-02 editor spec · 执行时间轴

> 编辑器实现任务书。编辑器需求见 [ab-02 UXD](../uxd/ab-02-exec-timeline.md)；引擎侧见 [runtime spec](../spec-runtime/ab-02-exec-timeline.md)。

## 1. 概述

时间轴编辑器实现：轨道视图、条目调色板、语义化 payloadA、试播。

## 2. 设计

- **轨道视图模型**：items 投影为 (kind, tick, duration) 图元；拖动写回 tick，Clip 拖尾写回 duration；不重排数组（消费按数组序，乱序仅警示）。
- **调色板**：11 种 kind 四组分类，拖入生成带必填字段的骨架并立即置为选中。
- **payloadA 语义化**：TagSignal→加/删开关；InputGate/TargetCollectionGate→请求 id（0 显示"用订单 id"）；EventGate→超时 tick（0 显示"无限等"）。
- **试播**：消费引擎干跑接口逐 tick 推进，与真实执行同一路径。

## 3. 精确语义与不变量

- 编辑器产出的 items 形状 = 加载器接受的形状（同源校验）。
- 试播终态与订单终态映射同源。

## 4. 依赖接口与验收
- 消费：AbilityExecSpec 结构、加载器编译入口、时间轴干跑推进接口。
- 验收：任意 kind 拖入保存后启动零错误；试播终态与实测一致；16 上限在 UI 先于启动报错出现。

**相关文档**：[ab-02 UXD](../uxd/ab-02-exec-timeline.md) · [ab-02 runtime spec](../spec-runtime/ab-02-exec-timeline.md)
