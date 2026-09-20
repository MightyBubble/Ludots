# attr-01 editor spec · 属性定义与约束

> 编辑器实现任务书。编辑器需求见 [attr-01 UXD](../uxd/attr-01-definition.md)；引擎侧见 [runtime spec](../spec-runtime/attr-01-definition.md)。

## 1. 概述

属性面板实现：合并视图、使用处索引、约束热改。

## 2. 设计

- **清单与索引**：注册表投影 + 全配置扫描交叉索引，保存时增量更新。
- **约束编辑**：首开 clampToBase 生成约束行（标重启级）；既有约束 min/max 走工作台热替换（带回滚）。
- **用量计数**：与启动注册同源。

## 3. 精确语义与不变量

- 索引集合与引擎注册表一致；热改判定与三限制同源。

## 4. 依赖接口与验收

- 消费：注册表枚举、约束表、工作台热替换管线。
- 验收：改 min 热生效可回滚；会话内新属性名被拒并提示。

**相关文档**：[attr-01 UXD](../uxd/attr-01-definition.md) · [attr-01 runtime spec](../spec-runtime/attr-01-definition.md)
