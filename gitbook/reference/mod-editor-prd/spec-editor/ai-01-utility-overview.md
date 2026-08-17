# ai-03 editor spec · AI 行为层总论

> 编辑器实现任务书。编辑器需求见 [ai-02 UXD](../uxd/ai-01-utility-overview.md)；引擎侧见 [runtime spec](../spec-runtime/ai-01-utility-overview.md)。

## 1. 概述

AI 总览面板实现：18 表清单投影、编译产物视图、接缝体检、来源分解。

## 2. 设计

- **表清单**：AiConfigCatalog 枚举 × 各表合并后条目计数；空表灰显不隐藏。
- **编译产物视图**：复用 LoadAndCompile 的 dry 结果（对齐 T11 dry-run）做只读投影；无法 dry 时退化为条目计数。
- **接缝体检**：静态扫描 GraphScore/SubmitOrder/AbilityKey 引用并对照注册表，编辑期即报断链。
- **来源条**：VFS 合并报告（cfg-02）按 AI 路径过滤。

## 3. 精确语义与不变量

- 条目计数与引擎合并结果同源（同走 ConfigPipeline 合并，不另写解析）。
- 体检判定的引用名解析规则与 loader 的 Ordinal 字典一致。

## 4. 依赖接口与验收

- 消费：AiConfigCatalog、ConfigPipeline 合并视图、AiCompiledRuntime 投影、Ability/OrderType 注册表。
- 验收：18 表一屏可见且空态不报错；效用半配置（无 profile）在编辑期被预检捕获；断链引用点击跳专篇。

**相关文档**：[ai-02 UXD](../uxd/ai-01-utility-overview.md) · [ai-02 runtime spec](../spec-runtime/ai-01-utility-overview.md)
