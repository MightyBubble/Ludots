# ai-05 editor spec · 目标过滤器

> 编辑器实现任务书。编辑器需求见 [ai-05 UXD](../uxd/ai-06-target-filters.md)；引擎侧见 [runtime spec](../spec-runtime/ai-06-target-filters.md)。

## 1. 概述

过滤器面板实现：op 链编辑器、参数动态表单、试验台重放。

## 2. 设计

- **op 链编辑器**：九 Kind 的参数 schema 驱动；拖动排序即改 Ops 数组顺序。
- **试验台**：对选定实体集本地重放判定链（复用判定函数的同源实现或 dry 调用），输出留/淘汰与原因码。
- **原因码表**：与 UtilityAiFilterRejectReason 同枚举映射成人话。
- **引用联动**：过滤器改动后提示受影响决策清单。

## 3. 精确语义与不变量

- 试验台判定与运行时逐 op 一致（含缺 Team/缺位置等淘汰路径）。
- 参数校验规则（正数、必填）与 loader 相同。

## 4. 依赖接口与验收

- 消费：target_filters 合并视图、tag/技能注册表、Team/位置组件查询、拒绝码枚举。
- 验收：九种 op 均可建-排-试-存；试验台淘汰原因与运行 trace 一致；非法参数被表单拦截。

**相关文档**：[ai-05 UXD](../uxd/ai-06-target-filters.md) · [ai-05 runtime spec](../spec-runtime/ai-06-target-filters.md)
