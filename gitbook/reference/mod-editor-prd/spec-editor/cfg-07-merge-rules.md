# cfg-07 editor spec · 合并案例

> 编辑器实现任务书。编辑器需求见 [cfg-07 UXD](../uxd/cfg-07-merge-rules.md)；引擎侧见 [runtime spec](../spec-runtime/cfg-07-merge-rules.md)；第一性需求见 [cfg-07 PRD](../prd/cfg-07-merge-rules.md)。

## 1. 概述

差异分类渲染与安全改组的实现；与 cfg-05 editor spec 的合并预览共用数据源。

## 2. 设计

- **五类差异分类**：以字段级溯源为输入，按"值类型为数组且被整组覆盖 → 整组替换"等规则归类着色。
- **危险字段元数据**：从各表加载器 schema 投射生成（数组且未登记可追加），驱动预警与确认框。
- **安全改组**：编辑数组元素 = 读合并结果整组 → 应用单元素变更 → 整组写回本 mod 分片；被替换元素清单来自溯源。

## 3. 精确语义与不变量

- 分类判定与合并语义一致；元数据清单随 schema 自动更新，禁止手抄副本。

## 4. 依赖接口与验收

- 消费：字段级溯源、各表 schema 投射、分片写回服务。
- 验收：十案例各做一次编辑器操作，启动后注册表结果与案例表"结果"列逐字一致。

**相关文档**：[cfg-07 UXD](../uxd/cfg-07-merge-rules.md) · [cfg-05 editor spec](cfg-05-config-pipeline.md)
