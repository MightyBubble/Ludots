# ai-04 editor spec · 效用输入

> 编辑器实现任务书。编辑器需求见 [ai-03 UXD](../uxd/ai-02-inputs.md)；引擎侧见 [runtime spec](../spec-runtime/ai-02-inputs.md)。

## 1. 概述

输入面板实现：Kind 驱动动态表单、引用选择器三件（图/tag/技能）、使用处索引。

## 2. 设计

- **动态表单**：Kind 枚举 → 字段 schema 映射；非法字段不渲染（未知字段不落盘）。
- **选择器**：GraphKey 列表过滤 RequireKind=Score 并前置写 op 黑名单；AbilityKey/Tag 直连注册表。
- **使用处索引**：扫描 decisions 考量的 Input 引用，保存时增量更新。
- **预检**：保存前跑与 loader 同源的校验子集（Kind 合法、参数正数、引用存在）。

## 3. 精确语义与不变量

- 表单产出的 JSON 与 CompileInputs 手工解析逐字段兼容（含默认值：Value=1、DefaultPriority=0）。
- 引用判定用 Ordinal 比较，与 loader 字典一致。

## 4. 依赖接口与验收

- 消费：图注册表（Score 过滤）、tag 注册表、AbilityDefinitionRegistry、decisions 合并视图。
- 验收：八种 Kind 均可建-引-删；写 op 图保存被拒；无引用输入删除不产生断链。

**相关文档**：[ai-03 UXD](../uxd/ai-02-inputs.md) · [ai-03 runtime spec](../spec-runtime/ai-02-inputs.md)
