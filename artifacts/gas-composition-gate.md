# GAS Composition Gate — GraphNodeOp Showcase P2 收尾

## 任务摘要
为 Epic #915 / PR #919 补齐剩余 GraphNodeOp 的 Showcase + registry `covered`，不新增 opcode / preset / profile DSL。

## 判断标准结论
**通过。** 新变体 = 已有 graph 节点的玩家可见演示与验收映射，不是新 enum/开关。

## 自审清单
- [x] 未新增 BuiltinHandler / EffectPresetType / profile schema
- [x] 未新增平行加载器或 inherit.mode
- [x] 复用现有 GraphControlFlow 前门 + CapabilityStandard GraphOps* Mod 模式
- [x] SSOT：`graph_node_op_coverage.registry.json` + `showcase.registry.json`
- [x] NO FALLBACK：缺覆盖保持 `runtime-only`，不得假标 covered

## 复用 / 新增
| 类型 | 项 |
|------|-----|
| 复用 | GraphControlFlowCompiler FrontDoor、现有 GraphOps*Mod、AbilityGraphSandbox、coverage registry 守卫 |
| 新增 Layer 0–2 | 无 |
| 新增 Mod | 仅缺族的 CapabilityStandardGraphOps* Showcase（数据驱动分镜） |
