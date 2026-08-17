# fx-10 runtime spec · 响应链

> 引擎实现任务书。第一性需求见 [fx-09 PRD](../prd/fx-07-response-chain.md)；现状见 [reference](../reference/fx-07-response-chain.md)。

## 1. 概述

响应链窗口状态机、四动作语义、从尾向前裁决与根预算表合同。

## 2. 设计

- 窗口状态机保持：None→Collect→WaitInput→Resolve；开窗=根提案+验证通过+声明参与；响应入队容量、连锁数量与深度、步数上限取事实页常量；步数熔断清队。
- Collect 与裁决保持：Hook 置取消、Modify 改窗口修正、Chain 派生新提案、PromptInput 置交互；裁决从尾向前，非根项在仍有剩余否决票时可被否决，通过者走计算相位后内联或实体化；连续两次空转关窗。
- 根预算表保持：开放寻址+时间戳 O(1) 清空；rootId==0 恒放行；超限抛扇出预算错误；事务内 checkpoint/提交/回滚三段。

## 3. 精确语义与不变量

- 修正只作用于窗口数值不直改属性；否决只影响本窗；根预算事务回滚后该根已消费额度一并回滚。

## 4. 迁移与治理

治理项 E5（todo/effect.md）：回应的动态图路径（ResponseGraphIds 约定槽位）无消费点、Collect 只读静态值——接通图路径或移除字段；接通属能力扩张须评审。

**变更记录**：v1（2026-08-15）：初版。

**相关文档**：[fx-09 PRD](../prd/fx-07-response-chain.md) · [reference](../reference/fx-07-response-chain.md)
