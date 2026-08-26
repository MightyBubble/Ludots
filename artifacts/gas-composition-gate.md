# GAS Composition Gate — 查询集合输出 / Effect 实例袋接线

## 任务摘要

按已合入合同 `query-graph-collection-outputs.md`，落地：
1. Query op：从 scope 的 `ActiveEffectContainer` 填充 `TargetList`（效果实例实体）
2. 面板 `PanelSubjectKind.EffectInstance`（及模板 subject 解析预留）+ 投影表面名走 `EffectTemplateIdRegistry`
3. 元素图可读效果剩余/总时长（atomic load op，非 profile 开关）
4. 面板 `inputs` / `collections.source` 装载期强校验
5. Showcase：单位 buff 条（仿 panel_entity_list）

## 判断标准结论

**通过。** 新变体是 **新增 graph 节点（QueryCollectActiveEffects、LoadEffectTiming）** 组合既有 `EntityCollection` 回写与面板投影，**不是** 新增 profile enum / preset 开关。

## 自审清单

| 项 | 结论 |
|---|---|
| 是否用 atomic op / graph 节点表达新行为 | 是：CollectActiveEffects + LoadEffectTiming |
| 是否避免新 `*_profiles.json` / preset 开关 | 是 |
| 是否复用 EntityCollection 回写链 | 是（效果实例仍是 Entity；subject 区分语义） |
| 是否禁止旁路扫容器作 SSOT | 是：正式路径图写出集合 |
| EffectTemplate 袋 | 本 PR 装载/subject 预留 fail-closed 或最小 Id 集合；优先打通实例袋竖切 |

## 复用 / 新增

| 类型 | 项 |
|---|---|
| 复用 | GraphReturnWriter、EntityCollectionStore、PanelListProjector 透传、ActiveEffectContainer、EffectTemplateIdRegistry、panel_entity_list 骨架 |
| 新增 Layer 0/图 op | `QueryCollectActiveEffects`、`LoadEffectTiming`（读 GameplayEffect 时长） |
| 新增面板 | `EffectInstance` subject 表面；`inputs`/`source` 装载校验 |
| 禁止 | 新 Effect buff profile DSL；伪造假实体装模板 id |

## 若不通过

N/A
