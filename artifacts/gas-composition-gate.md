# GAS Composition Gate — Case E whileActive（废 continuousQuery）

## 任务摘要

Case E 按配置报告彻底收口：按下/抬起裸边沿；profile 字段 `continuousQuery` 退役为 `whileActive`；`InteractionContextContinuousQuerySystem` 改名为 `InteractionContextWhileActiveSystem`（Case E §05 存活期调命中图）。不新增 profile enum / preset 开关语义，只改名对齐 Case E。

## 判断标准结论

**通过** — 变体是既有「context 存活期调图」挂载的字段更名与宿主改名，不是新 DSL。

## 自审清单

| 项 | 结论 |
|----|------|
| 新变体是 op 组合还是 profile enum？ | 字段更名；无新 enum |
| 是否重复造轮子？ | 否；沿用 GraphReturnWriter.Execute + DispatchCollectionEvent |
| 热路径分配？ | 无新增；原 scratch 字典保留 |
| 失败关闭？ | 配置仍写 continuousQuery → loader 抛错，指向 whileActive |

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | InteractionContextProfileRegistry、GraphReturnWriter、DispatchCollectionEvent |
| 改名 | continuousQuery→whileActive；ContinuousQuerySystem→WhileActiveSystem |
| 禁止 | 再引入 continuousQuery 或 Tap/Drag 作为 Case E 合同 |

## Case E 输入合同（本刀一并钉死）

- BoxSelectBegin / BoxSelectEnd（firesOn=release）
- 无 TapSelect / Drag / tap_commit
- boxing.whileActive.graph = graph.case_e.box_hit
