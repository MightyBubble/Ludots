# fx-13 runtime spec · 效果授予 Tag

> 引擎实现任务书。第一性需求见 [fx-13 PRD](../prd/fx-13-granted-tags.md)；现状见 [reference](../reference/fx-13-granted-tags.md)。

## 1. 概述
tag 贡献公式与差量合并合同：Compute 三实现、全量快照回滚、staged 授予与回收。

## 2. 设计
- Compute(stack) 三实现保持；amount/base 编译期钳计数上限。
- Grant/Revoke/Update 双轨（实体版 + TagCountContainer 版）保持全量 before 快照回滚；容器计数满计预算并抛 `GAS.TAG.ERR.TagCountOverflow`。
- 堆叠合并按 Compute(new)−Compute(old) 差量调 TagOps；失败先回滚 stack 与 GameplayEffect 再上抛。
- **治理项 E11**：GraphProgram 公式 loader 直接拒绝，其后参数处理与图解析为不可达死代码——接线 tag 贡献图评估器或删除死分支（todo/effect.md E11）。

## 3. 精确语义与不变量
- 目标某 tag 计数 = Σ 活跃效果按各自当前层数的贡献；回收量按移除时层数（无 stack 视为 1）。
- 授予与回收都是事务内 staged 副作用，失败不留半授予状态。

## 4. 迁移与治理
现状即基线；E11 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-13 PRD](../prd/fx-13-granted-tags.md) · [reference](../reference/fx-13-granted-tags.md)
