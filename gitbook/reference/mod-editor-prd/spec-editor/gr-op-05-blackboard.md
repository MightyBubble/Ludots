# gr-op-05 editor spec · 节点：黑板

> 编辑器实现任务书。编辑器需求见 [gr-op-05 UXD](../uxd/gr-op-05-blackboard.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-05-blackboard.md)。

## 1. 概述

键选择器与读写反查从 ConfigKeyRegistry 加全图扫描生成。

## 2. 设计

- **目录条目**：描述符表扫描六行；写节点按 kind 掩码置灰。
- **键选择器**：候选 = ConfigKeyRegistry 投影 ∪ 订单内置键表；按节点值类型过滤；写回 `blackboardKey` 字符串。
- **反查索引**：全配置扫描建键→使用处索引，保存时增量更新（与属性面板使用处索引同机制）。

## 3. 精确语义与不变量

- 选择器候选与注册表投影一致；类型过滤规则与编译器键类型校验同源。
- 反查索引与实际引用一致（含订单内置键的系统性写入方）。

## 4. 依赖接口与验收

- 消费：描述符表、ConfigKeyRegistry、订单内置键声明表。
- 验收：Effect 图六件全可用；Query 图全族置灰；键反查跳转准确无漏。

**相关文档**：[gr-op-05 UXD](../uxd/gr-op-05-blackboard.md) · [gr-op-05 runtime spec](../spec-runtime/gr-op-05-blackboard.md)
