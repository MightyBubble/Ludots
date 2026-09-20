# cfg-03 editor spec · 启动计划

> 编辑器实现任务书。编辑器需求见 [cfg-03 UXD](../uxd/cfg-03-launch-graph.md)；引擎侧见 [runtime spec](../spec-runtime/cfg-03-launch-graph.md)；第一性需求见 [cfg-03 PRD](../prd/cfg-03-launch-graph.md)。

## 1. 概述

编辑器运行体验的实现：组合 dry-run、一键启动、预设编辑。原则是复用启动器服务，不重写闭包推导。

## 2. 设计

- **组合预览**：消费启动器的 dry-run 入口（只算不写），返回闭包与顺序供 DAG 渲染；依赖 runtime spec 定义的 dry-run 交付。
- **运行按钮**：调用完整生成 + 启动链路；编辑器内启动与命令行启动产物指纹一致。
- **预设/绑定编辑**：两份仓库根文件的表单化编辑，规范化写回；别名与扫描根变更即时反映到组合预览候选。
- **运行配置**：项目级设置持久化（选择器 + 根 mod 集合），映射到预设或直接选择器。

## 3. 精确语义与不变量

- 预览顺序与实际启动顺序同源（同一闭包解析），不允许编辑器自算顺序。
- 预设写回往返无损；别名冲突在保存前检出。

## 4. 依赖接口与验收

- 消费：dry-run 解析、完整生成、启动入口、绑定/预设文件读写。
- 验收：编辑器启动与 CLI 同指纹；勾选组合的预览顺序与生成计划 `orderedModIds` 逐项一致。

**相关文档**：[cfg-03 UXD](../uxd/cfg-03-launch-graph.md) · [cfg-03 runtime spec](../spec-runtime/cfg-03-launch-graph.md)
