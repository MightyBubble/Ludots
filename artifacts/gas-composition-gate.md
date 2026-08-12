# GAS Composition Gate — Epic #915 P2 ATTR/EFFECT wave

## 任务摘要

新增 `CapabilityStandardGraphOpsAttrMod` showcase，用现有 Effect 图节点组合演示读血量/加伤/上效果/卸效果；不新增 preset enum 或 profile 开关。

## 判断标准结论

**通过** — 新变体为 graph op 组合（LoadAttribute / ModifyAttributeAdd / ApplyEffectTemplate / RemoveEffectTemplate 等），无新 profile DSL。

## 自审清单

| 项 | 结论 |
|----|------|
| 新变体是 op 组合还是 enum/开关？ | op 组合 |
| 是否复用现有 Registry/Pipeline？ | GraphProgramRegistry、GraphExecutor、GasGraphRuntimeApi、EffectRequestQueue |
| 是否新增平行加载器？ | 否；mod `graphs.json` 走标准 ConfigCatalog |
| 是否新增 BuiltinHandler/preset？ | 否；`Effect.GraphOpsAttr.Mark` 为最小 Buff 模板供 Apply/Remove 演示 |

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | GraphControlFlowCompiler、GraphExecutor、AttributeRegistry、EffectTemplateIdRegistry |
| 新增 Layer 2 | mod 内 4 张 Effect 图 + showcase runtime |
| 禁止 | 新 profile DSL、平行 catalog loader |
