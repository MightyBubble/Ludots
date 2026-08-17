# fx-20 runtime spec · 造单位

> 引擎实现任务书。第一性需求见 [fx-19 PRD](../prd/fx-16-unit-creation.md)；现状见 [reference](../reference/fx-16-unit-creation.md)。

## 1. 概述
批量生成合同：摆放与朝向计算、两种生成 Kind、出生效果链与归属开关。

## 2. 设计
- HandleCreateUnit：出生点由 `_ep.targetPos/origin` 系保留参数解析；count 次循环计算 Scatter 散布或 Circle 环形偏移与朝向角。
- 每次生成一个 RuntimeEntitySpawnRequest：templateId 走 Template Kind、unitType 走 UnitType Kind；OnSpawnEffectTemplateId、CopySourcePlayerOwner、LinkSourceAsParent 随单透传。
- 队伍归属固定 CopySourceTeam=1；事务内 StageSpawnRequest，非事务队列满抛错。

## 3. 精确语义与不变量
- 单位实体只经生成队列产生；count 次入队要么全部完成要么随容量错误中断。
- Scatter 不产生朝向（沿用模板/默认）；Circle 朝向由图案与起始角唯一确定。
- 出生效果链在实体落地后由生成管线触发，不在效果处理器内联执行。

## 4. 迁移与治理
现状即基线。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-19 PRD](../prd/fx-16-unit-creation.md) · [reference](../reference/fx-16-unit-creation.md)
