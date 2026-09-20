# ai-06 · 目标过滤器

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-06-target-filters.md)；编辑器需求见 [UXD](../uxd/ai-06-target-filters.md)；引擎实现见 [runtime spec](../spec-runtime/ai-06-target-filters.md)；editor spec 见 [editor spec](../spec-editor/ai-06-target-filters.md)；现状见 [reference](../reference/ai-06-target-filters.md)。

## 1. 定位

target filter 是决策的取景框：以 op 序列描述"什么算合法目标"——空间半径、敌我关系、tag 要求、层级掩码、距离上限、技能可施、最近攻击者。全部 op 顺序 AND。

## 2. 产品承诺

- **九种 op 一条链**：每 op 是一次判定，任一不过即淘汰该候选；MaxResults 限制产出数量。
- **拒绝有因**：每个 op 淘汰时携带专属拒绝码，trace 可见"为什么没打他"。
- **优先桶副作用**：HasAllTags 类 op 设计上可给 priorityBucket 加权（现状接线缺陷见 I4）。
- **可组合可复用**：一个过滤器被任意多个决策引用。

## 3. 运行行为

评估时对每个空间候选逐 op 判定：SourceSelf 把 actor 自身列为候选；Relationship 查双方 Team；HasAllTags/HasNoneTags 查目标 tag 容器；LayerAny 查层级掩码；DistanceMax 用平方距离比较；AbilityEligible 查技能可施；RecentAttacker 校验 UtilityAiCombatMemory 的 LastAttacker 存活与 TTL。通过者截断到 MaxResults。

## 4. 异常承诺

Ops 缺失、未知 Kind、RadiusCm/Mask/MaxCm/TtlSteps 非正、AbilityEligible 未配技能——启动失败并带 路径:id.Ops[n]。

**相关文档**：[配置说明](../config/ai-06-target-filters.md) · [ai-04](ai-04-decisions.md) · [ai-08](ai-08-stances-actuators.md)
