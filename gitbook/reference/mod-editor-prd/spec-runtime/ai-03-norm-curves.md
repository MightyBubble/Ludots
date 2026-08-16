# ai-02 runtime spec · 归一化与响应曲线

> 引擎实现任务书。第一性需求见 [ai-02 PRD](../prd/ai-03-norm-curves.md)；现状见 [reference](../reference/ai-03-norm-curves.md)。

## 1. 概述

两段整形器的编译与求值合同：三归一 × 三曲线，钳制饱和语义。

## 2. 设计

- Normalize/Curve 公式保持：Range=clamp((raw-Min)/(Max-Min))、RangeInverse=1-Range、Power=pow(v,e)、Inverse=1-v；Linear/Identity 直通。
- 编译校验保持：非 Identity 的 Max>Min、Exponent>0、未知 Kind 报错带路径。
- **治理项（引 todo/ai.md）**：I10——两张表无 schema；若扩 Kind（如 Sigmoid）须同步曲线预览与文档。

## 3. 精确语义与不变量

- 钳制只发生在归一化段：越 raw 窗口饱和为 0 或 1，曲线段不再钳制。
- 除零不可能：Max>Min 编译期保证。
- Min/Max/Exponent 均可为任意有限 float（Exponent>0）。

## 4. 迁移与治理

现状即基线；无待迁移面。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-02 PRD](../prd/ai-03-norm-curves.md) · [reference](../reference/ai-03-norm-curves.md)
