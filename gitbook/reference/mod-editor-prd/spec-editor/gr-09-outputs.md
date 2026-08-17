# gr-09 editor spec · Query 图输出

> 编辑器实现任务书。编辑器需求见 [gr-09 UXD](../uxd/gr-09-outputs.md)；引擎侧见 [runtime spec](../spec-runtime/gr-09-outputs.md)。

## 1. 概述

输出面板实现：outputs 编辑器、schema 同源校验、落点预览。

## 2. 设计

- 编辑器只提供合法组合（五类型 × 两去向的可行子集），非法组合在控件层不可选；保存仍走编译器 schema 校验兜底。
- source 候选由图内节点输出类型表推导，与编译器类型判定同源。
- 落点预览调用运行预览接口读回物化结果（集合/键值），只读。

## 3. 精确语义与不变量

- 面板不可构造的输出 = 编译器必拒的输出；两侧判定同源。
- outputs 数组往返无损。

## 4. 依赖接口与验收

- 消费：编译器 schema 校验、节点输出类型表、输出值存储统计、运行预览接口。
- 验收：两类 destination × 五类型全组合网格，编辑器与编译器接受集一致；预览读写不污染槽池。

**相关文档**：[gr-09 UXD](../uxd/gr-09-outputs.md) · [gr-09 runtime spec](../spec-runtime/gr-09-outputs.md)
