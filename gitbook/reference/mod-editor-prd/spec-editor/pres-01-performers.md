# pres-01 editor spec · 表现器档案

> 编辑器实现任务书。编辑器需求见 [pres-01 UXD](../uxd/pres-01-performers.md)；引擎侧见 [runtime spec](../spec-runtime/pres-01-performers.md)。

## 1. 概述

表现器编辑器实现：档案表单、行为槽编辑、资产选择器、预览视口。

## 2. 设计

- **档案模型**：主文件+分片的合并视图投影；保存时按来源片写回（主文件条目写主文件，分片条目写分片）。
- **行为编辑**：kind 决定动态表单（AssetBinding / instanced 批次 / 其余白名单 kind）；slot 唯一性编辑器侧先验。
- **资产选择器**：消费 pres-02 资产注册表投影，按 assetKind 过滤。
- **预览**：读资产注册表 + 剔除参数渲染占位/真实网格；不做引擎内嵌，用只读快照。

## 3. 精确语义与不变量

- 编辑器合并视图与引擎 ArrayById 深合并结果一致（同源合并器或同算法）。
- kind/slot 白名单来自引擎只读枚举，编辑器不自维护清单。
- 往返无损：读入→保存不改变语义等价的 JSON 结构。

## 4. 依赖接口与验收

- 消费：表现器注册表投影、资产注册表（pres-02）、kind/slot 枚举、冲突报告。
- 验收：换皮流（复制→换资产→保存）产物通过引擎启动校验；槽冲突在编辑期被拦。

**相关文档**：[pres-01 UXD](../uxd/pres-01-performers.md) · [pres-01 runtime spec](../spec-runtime/pres-01-performers.md)
