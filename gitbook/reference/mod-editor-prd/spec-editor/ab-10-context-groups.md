# ab-10 editor spec · 上下文组

> 编辑器实现任务书。编辑器需求见 [ab-10 UXD](../uxd/ab-10-context-groups.md)；引擎侧见 [runtime spec](../spec-runtime/ab-10-context-groups.md)。

## 1. 概述

上下文组编辑器实现：候选榜、两形字段组、沙盘同源打分预演。

## 2. 设计

- **候选榜**：candidates 投影，排序键为沙盘实时得分；胜者高亮。
- **两形字段组**：requiresTarget 开关切换必填集，编辑器侧先拦缺件（同加载器规则）。
- **图选择器**：按 kind 过滤（Validation/Score），数据源为图注册表。
- **沙盘**：调用打分消费的同源接口（空间查询+过滤+打分+tie-break），拖目标即时重算。

## 3. 精确语义与不变量

- 沙盘胜者与运行期裁决逐字一致（同一打分链）。
- 编辑器必填集 = 加载器必填集（同源）。

## 4. 依赖接口与验收
- 消费：组加载器校验、图注册表（含 kind）、打分消费接口（干跑）。
- 验收：构造硬过滤出局/悬停翻盘/平分 tie-break 三例，沙盘与实测一致；缺件候选在编辑器被拦。

**相关文档**：[ab-10 UXD](../uxd/ab-10-context-groups.md) · [ab-10 runtime spec](../spec-runtime/ab-10-context-groups.md)
