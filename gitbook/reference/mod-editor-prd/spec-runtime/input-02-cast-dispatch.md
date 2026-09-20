# input-02 runtime spec · 施法派发档案

> 引擎实现任务书。第一性需求见 [input-02 PRD](../prd/input-02-cast-dispatch.md)；现状见 [reference](../reference/input-02-cast-dispatch.md)。

## 1. 概述
派发合同：选人三式、效用评分、并行共享/顺序路由、方案级默认。

## 2. 设计
- 选人与路由保持：all/topN/cycle 三式；parallel 可共享单号（对接订单共享批量），sequential 按序提交。
- **治理项**：cycle 的推进入口 `NotifyAdvance` 生产零调用（仅测试调用）——`one_by_one` 档案退化为永远第一位演员。接线：订单接受回执处按 advanceOn 事件推进轮转游标（O8）。
- 评分器保持 utility 单实现；考虑因素语法（`因素:修饰`）加载期解析，未知因素即失败。
- 方案默认保持：档案 id 由 ControlSchemeRuntime.ActiveDefault 提供，未配置不派发。

## 3. 精确语义与不变量
- 同一命令一次裁决只消费一个档案。
- cycle 档案每次按 advanceOn 时机推进——不推进即缺陷，必须可诊断。
- 共享单号组要么整组同号，要么全无号。

## 4. 迁移与治理
现状即基线；O8 轮转接线为引擎任务，落地后回写 reference。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[input-02 PRD](../prd/input-02-cast-dispatch.md) · [reference](../reference/input-02-cast-dispatch.md)
