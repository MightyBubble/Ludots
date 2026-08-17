# ai-04 runtime spec · 效用输入

> 引擎实现任务书。第一性需求见 [ai-03 PRD](../prd/ai-02-inputs.md)；现状见 [reference](../reference/ai-02-inputs.md)。

## 1. 概述

八种输入 Kind 的编译与采样合同：一参一图二引用，感知只读。

## 2. 设计

- CompileInputs 结构保持：Kind OrdinalIgnoreCase 分派、arg0/graphId 装箱、Ordinal 字典登记。
- SampleInput 语义保持：越界 id 返 0；HasTag 查 GameplayTagContainer；AbilityReady 走 IsAbilityReady；GraphScore 走 ExecuteScoreGraph 且运行期再过安全校验。
- **治理项（引 todo/ai.md）**：I1——Constant 只支持整数（Value 走 TryReadInt），补 float 通道或文档化绕行；I2——inputs 的 Kind 大小写不敏感而 BT/HFSM 枚举 ignoreCase:false，统一为单一规则并文档化。
- GraphScore 的 GraphKey/GraphId 双通道互验语义保持（同 ability 双查）。

## 3. 精确语义与不变量

- inputId 越界 ⇒ 采样 0，不抛错。
- GraphScore 输入的图在编译期与运行期双重校验 RequireKind=Score 与写 op 黑名单。
- ids 字典 Ordinal：同名不同大小写视为两条。

## 4. 迁移与治理

现状即基线；I1/I2 处置入 todo/ai.md。新增 Kind 走编译+采样+文档三同步。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-03 PRD](../prd/ai-02-inputs.md) · [reference](../reference/ai-02-inputs.md)
