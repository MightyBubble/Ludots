# ord-06 runtime spec · 输入映射

> 引擎实现任务书。第一性需求见 [ord-06 PRD](../prd/ord-06-input-mappings.md)；现状见 [reference](../reference/ord-06-input-mappings.md)。

## 1. 概述
映射合同：一动作一映射、双路由形状、交互模式分层生效、候选择单。

## 2. 设计
- 校验链保持：actionId 全局唯一；`orderTypeKey` 与 `actorOrderRouting` 互斥；routing 禁技能标记与 Entities 目标；技能映射 `i0` 非负；auto/cursor 互斥且范围 >0；Grid 校验。
- 生效模式保持：映射覆盖优先、全局兜底；模式分派经命令意图仲裁逐帧解析。
- **治理项**：映射文件全部由 mod 携带且缺失时仅日志跳过——"按键无效却无错误"。改为引擎级可选声明：mod 清单声明携带则缺失 fail-fast，未声明则静默（O7）。

## 3. 精确语义与不变量
- 同一 actionId 在合并后的映射表中恰好出现一次。
- 候选择单确定性：同优先级按声明序，首个 match 命中即定。
- 用户覆写只影响绑定，不改变映射结构合同。

## 4. 迁移与治理
现状即基线；O7 缺失语义收紧为引擎任务，落地后回写 reference。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ord-06 PRD](../prd/ord-06-input-mappings.md) · [reference](../reference/ord-06-input-mappings.md)
