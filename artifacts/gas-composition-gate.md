# GAS Composition Gate — Self Review

- **Task / Issue**: ScreenRegionToEntities 增加可选 `tolerance` 显式传参端口；`graph.core.select_hit` 明文接线 ConstFloat(20)。owner 裁定：点击宽容应完全图化或明文传参组装，几何过滤器不得内部读配置（#1634 剥离判断语义的后续收口）。
- **Date**: 2026-09-22
- **Agent / Author**: ZCode（test-red-clearing session）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（既有 op 增加一个显式可选值输入端口 + 一条图数据接线）

结论: **PASS**

一句话理由: 拾取宽容从几何过滤器内部配置读取改为调用方显式传参（op 可选引脚 + 图内明文 ConstFloat），零新 enum/开关/管线。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 几何过滤签名 | 0 | FilterScreenRegionEntities(…, float tolerancePixels) 纯函数参数 |
| op 可选端口 | 0 | ScreenRegionToEntities tolerance 引脚（缺省 0xFF→0），描述表/校验/发射 |
| 图接线 | 2 | graph.core.select_hit + ConstFloat(20)→tolerance（数据） |
| 文档 | 3 | wiki 端口表 |

## 3. Reuse list

- 既有 byte.MaxValue 可选哨兵模式（SubmitCast B/C 同款）
- 既有 ValueInputKey 可选边校验模式（ReadMapVar source 同款）
- 既有接口默认重载模式（旧 4 参签名全兼容，测试直调不受影响）

## 4. New Layer 0 ops (if any)

N/A（无新 op，仅既有 op 加可选参数）

## 5. Transaction boundary

无（纯查询过滤参数化，无副作用）

## 6. Config SSOT

行为配置落在: graph JSON（graph.core.select_hit 的 ConstFloat(20) 明文值）

是否新增 JSON schema: **NO**
