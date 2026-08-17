# gr-04 editor spec · 图文档格式

> 编辑器实现任务书。编辑器需求见 [gr-03 UXD](../uxd/gr-02-document.md)；引擎侧见 [runtime spec](../spec-runtime/gr-02-document.md)。

## 1. 概述

画布视图模型与文档往返：编辑态结构与落盘 JSON 一一对应、无损往返。

## 2. 设计

- 视图模型直接映射顶层七字段与节点八族字段，不设界面私有字段；符号字段以引用模型（注册表 id）持有、落盘写名字。
- 连线合法性判定取自端口常量集与值类型表（引擎同源），编辑期即时拒绝。
- 导出统一走 FrontDoor：编辑器不自己拼最终 JSON 语义。

## 3. 精确语义与不变量

- 往返无损：读入 → 编辑（不改动）→ 落盘，字节级字段集合一致（键序规范化除外）。
- 编辑器不可能产出违反 FrontDoor 的文档。

## 4. 依赖接口与验收

- 消费：文档 schema 投影、端口常量表、FrontDoor。
- 验收：对全部主线资产图做往返测试零丢失；导出文档装载零诊断。

**相关文档**：[gr-03 UXD](../uxd/gr-02-document.md) · [gr-03 runtime spec](../spec-runtime/gr-02-document.md)
