# ai-02 editor spec · 归一化与响应曲线

> 编辑器实现任务书。编辑器需求见 [ai-02 UXD](../uxd/ai-03-norm-curves.md)；引擎侧见 [runtime spec](../spec-runtime/ai-03-norm-curves.md)。

## 1. 概述

整形器面板实现：动态参数表单 + 与运行时同源的曲线预览。

## 2. 设计

- **预览同源**：预览直接复用 Normalize/Curve 公式的同一实现（抽公共小函数），不写第二份公式。
- **前置校验**：Max>Min、Exponent>0 在表单层拦截，错误文案与 loader 对齐。
- **被引用索引**：扫描 decisions 考量引用，删除前检查。

## 3. 精确语义与不变量

- 预览输出与运行时求值逐点相等（同函数保证）。
- 表单落盘字段名与 CompileNormalizations/CompileCurves 解析名一致（Kind/Min/Max/Exponent）。

## 4. 依赖接口与验收

- 消费：两张表的合并视图、decisions 引用扫描、共享求值函数。
- 验收：三种归一化×三种曲线的预览与运行一致；非法参数无法保存；被引用条目删除被阻。

**相关文档**：[ai-02 UXD](../uxd/ai-03-norm-curves.md) · [ai-02 runtime spec](../spec-runtime/ai-03-norm-curves.md)
